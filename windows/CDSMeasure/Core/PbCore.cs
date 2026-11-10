using System;
using System.Collections.Generic;
using System.Linq;

namespace CDSMeasure.Core;

/// <summary>Ena meritna točka sweepa: čas (s), izmerjena frekvenca (Hz) in nivo tona (dBFS).</summary>
public readonly record struct FrPoint(double T, double F, double Db);

/// <summary>
/// Frekvenčni odziv iz sweepa (ali stopničastih tonov), do 2 kanala.
/// Postopek (preverjen v Pythonu s sintetičnim sweepom z znano karakteristiko, napaka v glavnem &lt; 0,2 dB):
/// 1. decimacija 192 → 96 kHz, 2. okna 0,68 s (Hann, 75 % prekrivanje), 3. v vsakem oknu najdemo ton
/// (drseča vsota moči, 6 % frekvence), 4. moč tona = vsota moči okoli vrha minus šum (neodvisno od hitrosti sweepa),
/// frekvenca = središče moči, 5. okna brez spreminjanja frekvence (brum, pilot) zavržemo (le pri sweepu),
/// 6. nivo v pasu 1/3 oktave = regresija nivo[dB] proti log f, vrednost pri sredinski frekvenci.
/// Push() dela sinhrono; za zajem v živo uporabi PbRecorder (delovna nit).
/// </summary>
public sealed class PbCore
{
    public static readonly double[] Centers =
        Enumerable.Range(-18, 33).Select(n => 1000.0 * Math.Pow(10, n / 10.0)).ToArray();          // 15,8 Hz … 25,1 kHz (kot Yoshimasa)
    public static readonly string[] Labels =
    {
        "16", "20", "25", "31.5", "40", "50", "63", "80", "100", "125", "160", "200", "250", "315", "400", "500", "630", "800",
        "1k", "1.25k", "1.6k", "2k", "2.5k", "3.15k", "4k", "5k", "6.3k", "8k", "10k", "12.5k", "16k", "20k", "25k"
    };
    public const int RefBand = 18;                                // 1 kHz

    /// <summary>Sweep: samo okna s spreminjajočo se frekvenco. Stopnice: vsa okna z jasnim tonom.</summary>
    public bool StepsMode { get; set; }

    public int Fs { get; }
    public int Fd { get; }                 // frekvenca po decimaciji
    public int N { get; }                  // dolžina okna (vzorci)
    public int Channels { get; }

    private const double MinRateOctPerS = 0.02;                   // najmanjše spreminjanje frekvence za sweep
    private const double MinSnrDb = 20;

    private readonly int _dec, _hop, _half, _lo, _hi;
    private readonly double _bw, _sw2;
    private readonly double[] _win, _h;
    private readonly int[] _hw;                                   // polovična širina drsečega seštevanja po koših
    private readonly Chan[] _ch;
    private readonly object _lock = new();

    private sealed class Raw
    {
        public double T; public int K; public double Snr, Med; public double[]? P;
    }

    private sealed class Chan
    {
        public double[] Hist = Array.Empty<double>(); public int HPos, Phase;
        public double[] Ring = Array.Empty<double>(); public int RPos; public long Total;
        public readonly List<Raw> Raws = new();
        public int Finalized;
        public readonly List<FrPoint> Points = new();
        public double Peak;
    }

    public PbCore(int fs, int channels = 2)
    {
        Fs = fs; Channels = Math.Clamp(channels, 1, 2);
        _dec = fs >= 160000 ? 2 : 1;
        Fd = fs / _dec;
        int n = 1; while (n < 0.6 * Fd) n <<= 1;
        N = n; _hop = N / 4; _half = N / 2;
        _bw = (double)Fd / N;
        _lo = (int)(14 / _bw); _hi = Math.Min(_half, (int)(27000 / _bw));
        _win = new double[N];
        double sw2 = 0;
        for (int i = 0; i < N; i++) { _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)); sw2 += _win[i] * _win[i]; }
        _sw2 = sw2;
        _hw = new int[_half + 1];
        for (int k = 0; k <= _half; k++) _hw[k] = Math.Max(3, (int)(0.06 * k));

        // FIR za decimacijo 2 (Kaiser, β = 9): prepušča do ~46 kHz pri 192 kHz
        const int taps = 63;
        _h = new double[taps];
        double fc = 0.24, sum = 0; int m = taps / 2;
        for (int i = 0; i < taps; i++)
        {
            double x = i - m;
            double sinc = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
            double r = 2.0 * i / (taps - 1) - 1.0;
            double kw = Bessel0(9.0 * Math.Sqrt(Math.Max(0, 1 - r * r))) / Bessel0(9.0);
            _h[i] = sinc * kw; sum += _h[i];
        }
        for (int i = 0; i < taps; i++) _h[i] /= sum;

        _ch = new Chan[Channels];
        for (int c = 0; c < Channels; c++)
            _ch[c] = new Chan { Hist = new double[taps], Ring = new double[N] };
    }

    private static double Bessel0(double x)
    {
        double s = 1, t = 1;
        for (int k = 1; k < 40; k++) { t *= (x / (2 * k)) * (x / (2 * k)); s += t; }
        return s;
    }

    /// <summary>Čas posnetka v sekundah.</summary>
    public double Seconds { get { lock (_lock) return _ch[0].Total * (double)_dec / Fs; } }

    /// <summary>Zadnji nivo vrha (dBFS) po kanalih, za prikaz vhodnega nivoja.</summary>
    public double PeakDb(int ch) { lock (_lock) return 20 * Math.Log10(Math.Max(_ch[ch].Peak, 1e-9)); }

    /// <summary>Število točk (za zaznavo konca sweepa).</summary>
    public int PointCount { get { lock (_lock) return _ch.Max(c => c.Points.Count); } }

    public FrPoint[] Points(int ch) { lock (_lock) return _ch[ch].Points.ToArray(); }

    /// <summary>Prenese vzorce (prepleteni), sinhrono. channels = število kanalov v bufferju.</summary>
    public void Push(float[] interleaved, int frames, int channels)
    {
        for (int c = 0; c < Channels && c < channels; c++)
        {
            var st = _ch[c];
            double pk = 0;
            for (int n = 0; n < frames; n++)
            {
                double x = interleaved[n * channels + c];
                double ax = Math.Abs(x); if (ax > pk) pk = ax;
                if (_dec == 1) { Emit(c, st, x); continue; }
                st.Hist[st.HPos] = x;
                st.HPos++; if (st.HPos == st.Hist.Length) st.HPos = 0;
                if (++st.Phase < _dec) continue;
                st.Phase = 0;
                double y = 0; int idx = st.HPos;
                for (int k = 0; k < _h.Length; k++)
                {
                    idx--; if (idx < 0) idx = st.Hist.Length - 1;
                    y += _h[k] * st.Hist[idx];
                }
                Emit(c, st, y);
            }
            lock (_lock) st.Peak = Math.Max(pk, st.Peak * 0.97);
        }
    }

    private void Emit(int c, Chan st, double y)
    {
        st.Ring[st.RPos] = y;
        st.RPos = (st.RPos + 1) & (N - 1);
        st.Total++;
        if (st.Total >= N && (st.Total - N) % _hop == 0) Frame(c, st);
    }

    private void Frame(int c, Chan st)
    {
        var re = new double[N]; var im = new double[N];
        int p = st.RPos;                                           // najstarejši vzorec
        for (int i = 0; i < N; i++) re[i] = st.Ring[(p + i) & (N - 1)] * _win[i];
        Spectrum.Fft(re, im);
        var P = new double[_half + 1];
        for (int k = 0; k <= _half; k++) P[k] = re[k] * re[k] + im[k] * im[k];

        // drseča vsota moči okoli vsakega koša, vrh = najmočnejši
        var cs = new double[_half + 2];
        for (int k = 0; k <= _half; k++) cs[k + 1] = cs[k] + P[k];
        int bestK = _lo; double bestS = -1;
        for (int k = _lo; k < _hi; k++)
        {
            int h = _hw[k];
            double s = cs[Math.Min(k + h + 1, _half + 1)] - cs[Math.Max(k - h, 0)];
            if (s > bestS) { bestS = s; bestK = k; }
        }
        // mediana moči (šum) po vsakem 4. košu
        int cnt = Math.Max(1, (_hi - _lo + 3) / 4);
        var tmp = new double[cnt]; int ti = 0;
        for (int k = _lo; k < _hi && ti < cnt; k += 4) tmp[ti++] = P[k];
        Array.Sort(tmp, 0, ti);
        double med = ti > 0 ? tmp[ti / 2] : 0;
        double snr = bestS / Math.Max(1e-30, med * (2 * _hw[bestK] + 1));
        double t = (st.Total - N / 2.0) / Fd;

        var raw = new Raw { T = t, K = bestK, Snr = snr, Med = med, P = snr >= Math.Pow(10, MinSnrDb / 10) ? P : null };
        lock (_lock)
        {
            st.Raws.Add(raw);
            while (st.Finalized + 3 < st.Raws.Count) FinalizeFrame(st, st.Finalized++);
        }
    }

    /// <summary>Po koncu zajema: obdela še zadnja okna.</summary>
    public void Flush()
    {
        lock (_lock)
            foreach (var st in _ch)
                while (st.Finalized < st.Raws.Count) FinalizeFrame(st, st.Finalized++);
    }

    private void FinalizeFrame(Chan st, int i)
    {
        var r = st.Raws[i];
        var P = r.P; r.P = null;
        if (P == null) return;
        int j0 = Math.Max(0, i - 3), j1 = Math.Min(st.Raws.Count - 1, i + 3);
        double dt = st.Raws[j1].T - st.Raws[j0].T;
        double dk = Math.Abs(st.Raws[j1].K - st.Raws[j0].K);
        double rate = j1 > j0 ? dk / Math.Max(1.0, r.K) / Math.Log(2) / dt : 0;     // oktav/s
        if (!StepsMode && rate < MinRateOctPerS) return;                           // brum / stalen ton ni sweep
        double slope = j1 > j0 ? dk * _bw / dt : 0;                                // Hz/s
        double smear = StepsMode ? 0 : slope * N / Fd;
        int K = Math.Max(_hw[r.K], (int)Math.Ceiling(smear / 2 / _bw * 1.25) + 6);

        int kc = r.K, a = 0, b = 0;
        for (int it = 0; it < 3; it++)
        {
            a = Math.Max(0, kc - K); b = Math.Min(_half, kc + K);
            double ps = 0, pk = 0;
            for (int k = a; k <= b; k++) { double v = Math.Max(P[k] - r.Med, 0); ps += v; pk += k * v; }
            if (ps <= 0) break;
            kc = (int)Math.Round(pk / ps);
        }
        a = Math.Max(0, kc - K); b = Math.Min(_half, kc + K);
        double pw = 0, wsum = 0;
        for (int k = a; k <= b; k++) { double v = Math.Max(P[k] - r.Med, 0); pw += v; wsum += k * v; }
        if (pw <= 0) return;
        double f = wsum / pw * _bw;
        double db = 10 * Math.Log10(4 * pw / ((double)N * _sw2));
        st.Points.Add(new FrPoint(r.T, f, db));
    }

    /// <summary>Peak Hold (kot Yoshimasa): najvišji nivo tona, ki ga je sweep dosegel v pasu 1/3 oktave. NaN, če ni točk.</summary>
    public static double[] BandsPeak(FrPoint[] pts)
    {
        var res = new double[Centers.Length];
        Array.Fill(res, double.NaN);
        foreach (var p in pts)
        {
            for (int bi = 0; bi < Centers.Length; bi++)
            {
                double fc = Centers[bi];
                if (p.F < fc * Math.Pow(10, -0.05) || p.F >= fc * Math.Pow(10, 0.05)) continue;
                if (double.IsNaN(res[bi]) || p.Db > res[bi]) res[bi] = p.Db;
                break;
            }
        }
        return res;
    }

    /// <summary>Nivo [dB] v vsakem od 33 pasov 1/3 oktave (NaN, če ni točk). Regresija nivo proti log f, vrednost pri sredini.</summary>
    public static double[] Bands(FrPoint[] pts)
    {
        var res = new double[Centers.Length];
        for (int bi = 0; bi < Centers.Length; bi++)
        {
            double fc = Centers[bi], lo = fc * Math.Pow(10, -0.05), hi = fc * Math.Pow(10, 0.05);
            double sx = 0, sy = 0, sxx = 0, sxy = 0, xmin = double.MaxValue, xmax = double.MinValue; int n = 0;
            foreach (var p in pts)
            {
                if (p.F < lo || p.F >= hi) continue;
                double x = Math.Log10(p.F / fc);
                sx += x; sy += p.Db; sxx += x * x; sxy += x * p.Db; n++;
                if (x < xmin) xmin = x; if (x > xmax) xmax = x;
            }
            if (n == 0) { res[bi] = double.NaN; continue; }
            double den = n * sxx - sx * sx;
            if (n >= 3 && xmax - xmin > 0.02 && Math.Abs(den) > 1e-12)
            {
                double slope = (n * sxy - sx * sy) / den;
                res[bi] = (sy - slope * sx) / n;                   // vrednost pri x = 0 (sredinska frekvenca)
            }
            else res[bi] = sy / n;
        }
        return res;
    }
}

/// <summary>Delovna nit: ASIO povratni klic samo odloži vzorce v vrsto, analiza teče v ločeni niti.</summary>
public sealed class PbRecorder : IDisposable
{
    public PbCore Core { get; }
    private readonly System.Collections.Concurrent.BlockingCollection<(float[] D, int N, int Ch)> _q = new(2048);
    private readonly System.Threading.Thread _t;
    private bool _stopped;

    public PbRecorder(PbCore core)
    {
        Core = core;
        _t = new System.Threading.Thread(Work) { IsBackground = true, Name = "PbRecorder" };
        _t.Start();
    }

    public void Feed(float[] buf, int n, int ch) { if (!_stopped) _q.TryAdd((buf, n, ch)); }

    private void Work()
    {
        foreach (var (d, n, ch) in _q.GetConsumingEnumerable()) Core.Push(d, n, ch);
    }

    /// <summary>Konča zajem, počaka, da se vrsta izprazni, in obdela zadnja okna.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _q.CompleteAdding();
        _t.Join();
        Core.Flush();
    }

    public void Dispose() { try { Stop(); } catch { } }
}

#if !AVALONIA
/// <summary>Analiza posnetka (WAV, FLAC, MP3 …) z istim jedrom kot zajem v živo.</summary>
public static class PbFile
{
    public static PbCore Analyze(string path, bool stepsMode)
    {
        using var rd = new NAudio.Wave.AudioFileReader(path);
        int ch = rd.WaveFormat.Channels, fs = rd.WaveFormat.SampleRate;
        var core = new PbCore(fs, ch) { StepsMode = stepsMode };
        var buf = new float[fs * ch / 2 / ch * ch];
        int n;
        while ((n = rd.Read(buf, 0, buf.Length)) > 0) core.Push(buf, n / ch, ch);
        core.Flush();
        return core;
    }
}
#endif
