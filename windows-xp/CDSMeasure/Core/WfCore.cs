using System;
using System.Collections.Generic;
using System.Numerics;

namespace CDSMeasure.Core;

/// <summary>Rezultat meritve (osvežuje se ~10× na sekundo).</summary>
public struct WfSnapshot
{
    public bool Valid;          // je testni ton prisoten (nivo dovolj visok)
    public bool Settling;       // filtri se še ustaljujejo (prve ~4 s)
    public double LevelDb;      // nivo testnega tona [dBFS]
    public double SpeedHz;      // povprečna frekvenca zadnje sekunde
    public double SpeedPct;     // odstopanje od reference [%]
    public double Wrms;         // utežen RMS (JIS) [%]
    public double Wpeak;        // utežen vrh: vrednost, ki je presežena 5 % časa (AES6 / DIN 2σ) [%]
    public double Urms;         // neutežen RMS [%]
}

/// <summary>
/// Pretočno jedro W&amp;F in hitrosti (enak postopek kot Python prototip wf_core.py, preverjen s sintetičnim FM tonom):
/// 1. kvadraturno mešanje z referenco (3000/3150 Hz), 2. FIR 500 Hz + decimacija na 2 kHz,
/// 3. trenutna frekvenca iz razlike faz, 4. utežitev CCIR 409-1 / JIS (HP2 0,4487 Hz Q 0,6471 · HP1 1,3967 Hz · LP1 11,498 Hz,
/// normirano na 4 Hz) in neutežen filter (HP 0,2 Hz · LP 200 Hz).
/// Procesira se v ASIO niti; rezultat prebere uporabniški vmesnik prek Snapshot.
/// </summary>
public sealed class WfCore
{
    public const double OutFs = 400.0;               // frekvenca izhodnega signala za časovni graf in spekter
    private const double Fsd = 2000.0;               // decimirana frekvenca

    private readonly double _fs, _ref;
    private readonly int _dec;
    private readonly int _ntaps = 2001;
    private readonly double[] _h;
    private readonly double[] _bufI, _bufQ;
    private int _pos, _decCount;
    private double _phase;
    private readonly double _dPhase;

    private double _pI, _pQ;
    private bool _havePrev, _primed;
    private long _outCount;
    private double _levelAmp;
    private double _peakDb = -200;                // najvišji zaznani nivo tona (počasi upada)
    public const double MinLevelDb = -45.0;       // absolutni prag nivoja tona [dBFS]
    public const double BandHz = 100.0;           // izmerjena frekvenca mora biti v ±100 Hz od reference (3000 Hz → 2900–3100 Hz)
    public const double DropDb = 15.0;            // dovoljen padec pod najvišji nivo [dB]

    private readonly Chain _w, _u;
    private readonly double _wGain;
    private readonly Chain _raw1, _raw2;             // Butterworth 4. reda (120 Hz) pred decimacijo na 400 Hz
    private int _rawCount;

    private readonly int _win;                       // 1 s v decimiranih vzorcih
    private readonly double[] _rSpeed, _rW2, _rU2;
    private readonly float[] _rAbsW;                 // 3 s za percentil
    private int _ri, _ra;
    private double _sSpeed, _sW2, _sU2;
    private int _filled, _filledAbs;

    private readonly object _lock = new();
    private WfSnapshot _snap;
    private readonly List<float> _rawOut = new();

    public WfCore(double sampleRate, double refHz)
    {
        _fs = sampleRate; _ref = refHz;
        _dec = Math.Max(1, (int)Math.Round(sampleRate / Fsd));
        _dPhase = 2 * Math.PI * refHz / sampleRate;

        _h = new double[_ntaps];
        double fc = 500.0, sum = 0;
        for (int i = 0; i < _ntaps; i++)
        {
            double n = i - (_ntaps - 1) / 2.0;
            double x = 2 * fc / _fs * n;
            double sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            double blk = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (_ntaps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (_ntaps - 1));
            _h[i] = sinc * 2 * fc / _fs * blk;
            sum += _h[i];
        }
        for (int i = 0; i < _ntaps; i++) _h[i] /= sum;
        _bufI = new double[2 * _ntaps];
        _bufQ = new double[2 * _ntaps];

        double fsd = _fs / _dec;
        _w = new Chain(new[] { (Biquad.Hp2(0.4487, 0.6471, fsd), 0.0), (Biquad.Hp(1.3967, fsd), 0.0), (Biquad.Lp(11.498, fsd), 1.0) });
        _u = new Chain(new[] { (Biquad.Hp(0.2, fsd), 0.0), (Biquad.Lp(200.0, fsd), 1.0) });
        _wGain = 1.0 / _w.Gain(4.0, fsd);
        _raw1 = new Chain(new[] { (Biquad.Lp2(120.0, 0.5412, fsd), 1.0) });
        _raw2 = new Chain(new[] { (Biquad.Lp2(120.0, 1.3066, fsd), 1.0) });

        _win = (int)Math.Round(fsd);
        _rSpeed = new double[_win]; _rW2 = new double[_win]; _rU2 = new double[_win];
        _rAbsW = new float[_win * 3];
    }

    /// <summary>Obdela blok prepletenih vzorcev (frames × channels), uporabi en kanal.</summary>
    public void Process(float[] interleaved, int frames, int channels, int channel)
    {
        for (int n = 0; n < frames; n++)
        {
            double x = interleaved[n * channels + channel];

            // kvadraturno mešanje
            double c = Math.Cos(_phase), s = Math.Sin(_phase);
            _phase += _dPhase; if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            _bufI[_pos] = _bufI[_pos + _ntaps] = x * c;
            _bufQ[_pos] = _bufQ[_pos + _ntaps] = -x * s;
            _pos++; if (_pos == _ntaps) _pos = 0;

            if (++_decCount < _dec) continue;
            _decCount = 0;

            // FIR (simetričen): okno [_pos, _pos + ntaps)
            double zi = 0, zq = 0;
            int o = _pos;
            for (int k = 0; k < _ntaps; k++) { double hk = _h[k]; zi += hk * _bufI[o + k]; zq += hk * _bufQ[o + k]; }
            OnDecimated(zi, zq);
        }
    }

    private void OnDecimated(double zi, double zq)
    {
        double mag = 2 * Math.Sqrt(zi * zi + zq * zq);              // amplituda tona
        _levelAmp += (mag - _levelAmp) * 0.01;

        if (!_havePrev) { _pI = zi; _pQ = zq; _havePrev = true; return; }
        double re = zi * _pI + zq * _pQ, im = zq * _pI - zi * _pQ;
        _pI = zi; _pQ = zq;
        double fsd = _fs / _dec;
        double f = _ref + Math.Atan2(im, re) * fsd / (2 * Math.PI);
        double d = (f - _ref) / _ref * 100.0;                       // odstopanje [%]

        if (!_primed) { _w.Prime(d); _u.Prime(d); _raw1.Prime(d); _raw2.Prime(d); _primed = true; }
        double wv = _w.Step(d) * _wGain;
        double uv = _u.Step(d);

        // drseča okna (1 s)
        _sSpeed += f - _rSpeed[_ri]; _rSpeed[_ri] = f;
        _sW2 += wv * wv - _rW2[_ri]; _rW2[_ri] = wv * wv;
        _sU2 += uv * uv - _rU2[_ri]; _rU2[_ri] = uv * uv;
        _ri++; if (_ri == _win) _ri = 0;
        _rAbsW[_ra] = (float)Math.Abs(wv); _ra++; if (_ra == _rAbsW.Length) _ra = 0;
        if (_filled < _win) _filled++;
        if (_filledAbs < _rAbsW.Length) _filledAbs++;

        // izhod za graf in spekter: LP 120 Hz (4. red), decimacija 2000 → 400 Hz
        double r = _raw2.Step(_raw1.Step(d));
        if (++_rawCount >= (int)Math.Round(fsd / OutFs))
        {
            _rawCount = 0;
            lock (_lock) _rawOut.Add((float)r);
        }

        _outCount++;
        if (_outCount % (int)(fsd / 10) == 0) Publish();            // 10× na sekundo
    }

    private void Publish()
    {
        double levelDb = 20 * Math.Log10(Math.Max(_levelAmp, 1e-9));
        // Ton je veljaven, če je dovolj glasen in največ 15 dB pod nivojem, ki smo ga že zaznali
        // (počasno upadanje 0,2 dB/s). Tako motnje iz okolice po koncu kasete ne štejejo kot signal.
        _peakDb = _peakDb < -150 ? levelDb : Math.Max(levelDb, _peakDb - 0.02);
        var s = new WfSnapshot { LevelDb = levelDb, Valid = levelDb > MinLevelDb && levelDb > _peakDb - DropDb
                                  && _filled > 0 && Math.Abs(_sSpeed / _filled - _ref) <= BandHz, Settling = _outCount < 4 * Fsd };
        if (_filled > 0)
        {
            s.SpeedHz = _sSpeed / _filled;
            s.SpeedPct = (s.SpeedHz / _ref - 1) * 100;
            s.Wrms = Math.Sqrt(Math.Max(0, _sW2) / _filled);
            s.Urms = Math.Sqrt(Math.Max(0, _sU2) / _filled);
        }
        if (_filledAbs > 0)
        {
            var tmp = new float[_filledAbs];
            Array.Copy(_rAbsW, tmp, _filledAbs);
            Array.Sort(tmp);
            s.Wpeak = tmp[Math.Min(tmp.Length - 1, (int)(0.95 * tmp.Length))];
        }
        lock (_lock) _snap = s;
    }

    public WfSnapshot Snapshot { get { lock (_lock) return _snap; } }

    /// <summary>Prenese nabrane vzorce odstopanja (400 Hz, v % glede na referenco).</summary>
    public int DrainRaw(List<float> dst)
    {
        lock (_lock)
        {
            int n = _rawOut.Count;
            if (n > 0) { dst.AddRange(_rawOut); _rawOut.Clear(); }
            return n;
        }
    }

    // ---------- filtri ----------
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;
        public readonly double[] B, A;

        private Biquad(double[] b, double[] a)
        {
            B = b; A = a;
            _b0 = b[0]; _b1 = b[1]; _b2 = b.Length > 2 ? b[2] : 0;
            _a1 = a[1]; _a2 = a.Length > 2 ? a[2] : 0;
        }

        public static Biquad Lp(double fc, double fs)
        {
            double K = Math.Tan(Math.PI * fc / fs);
            return new Biquad(new[] { K / (1 + K), K / (1 + K) }, new[] { 1.0, (K - 1) / (K + 1) });
        }
        public static Biquad Hp(double fc, double fs)
        {
            double K = Math.Tan(Math.PI * fc / fs);
            return new Biquad(new[] { 1 / (1 + K), -1 / (1 + K) }, new[] { 1.0, (K - 1) / (K + 1) });
        }
        public static Biquad Hp2(double f0, double q, double fs)
        {
            double K = Math.Tan(Math.PI * f0 / fs), n = 1 + K / q + K * K;
            return new Biquad(new[] { 1 / n, -2 / n, 1 / n }, new[] { 1.0, 2 * (K * K - 1) / n, (1 - K / q + K * K) / n });
        }
        public static Biquad Lp2(double fc, double q, double fs)
        {
            double K = Math.Tan(Math.PI * fc / fs), n = 1 + K / q + K * K;
            return new Biquad(new[] { K * K / n, 2 * K * K / n, K * K / n }, new[] { 1.0, 2 * (K * K - 1) / n, (1 - K / q + K * K) / n });
        }

        public void Prime(double x0, double dc) { _x1 = _x2 = x0; _y1 = _y2 = x0 * dc; }

        public double Step(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }

    private sealed class Chain
    {
        private readonly (Biquad q, double dc)[] _st;
        public Chain((Biquad, double)[] st) { _st = st; }

        public void Prime(double x0)
        {
            double v = x0;
            foreach (var (q, dc) in _st) { q.Prime(v, dc); v *= dc; }
        }
        public double Step(double x)
        {
            foreach (var (q, _) in _st) x = q.Step(x);
            return x;
        }
        public double Gain(double f, double fs)
        {
            var z = Complex.Exp(new Complex(0, -2 * Math.PI * f / fs));
            Complex h = 1;
            foreach (var (q, _) in _st)
            {
                Complex num = 0, den = 0, zk = 1;
                for (int k = 0; k < q.B.Length; k++) { num += q.B[k] * zk; zk *= z; }
                zk = 1;
                for (int k = 0; k < q.A.Length; k++) { den += q.A[k] * zk; zk *= z; }
                h *= num / den;
            }
            return h.Magnitude;
        }
    }
}
