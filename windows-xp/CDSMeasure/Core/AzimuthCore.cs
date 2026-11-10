using System;

namespace CDSMeasure.Core;

/// <summary>Rezultat ene analize azimuta (nespremenljiv objekt, zamenja se ob vsaki novi analizi).</summary>
public sealed class AzimuthResult
{
    public bool Valid;                 // ton je zaznan v obeh kanalih
    public double FreqHz;              // izmerjena frekvenca tona
    public double LevelLDb, LevelRDb;  // nivo tona v kanalih (dBFS)
    public double SumDb;               // nivo vsote L+R (dBFS)
    public double PhaseDeg;            // fazna razlika L − R, od −180 do +180
    public double DelayUs;             // ekvivalentni časovni zamik v mikrosekundah
    public double PeakLDb, PeakRDb;    // najvišji vzorec od zadnje analize (dBFS)
}

/// <summary>
/// Azimut glave pri predvajanju: iz stereo zajema testnega tona (npr. 10, 12,5 ali 15 kHz) izračuna fazno razliko med L in D.
/// Pri pravilnem azimutu je razlika 0. Metoda: Hannovo okno + FFT obeh kanalov, navzkrižni spekter v okolici vrha
/// (faza ne zavisi od točne frekvence, zato hitrost traku ne moti), glajenje enotskega fazorja.
/// </summary>
public sealed class AzimuthCore
{
    private readonly int _rate, _n, _hop, _ch;
    private readonly float[] _l, _r;
    private readonly double[] _win, _re1, _im1, _re2, _im2;
    private readonly double _sw;
    private int _pos, _filled, _since;
    private float _pkL, _pkR;
    private readonly object _lock = new();
    private AzimuthResult _last = new();
    private bool _have;
    private double _ux, _uy, _dbL, _dbR, _dbS, _fHz;

    /// <summary>Nazivna frekvenca tona v Hz; 0 = samodejno (najmočnejši ton med 5 in 18 kHz).</summary>
    public double NominalHz { get; set; }
    public int Rate => _rate;
    public int Channels => _ch;

    public AzimuthCore(int rate, int channels)
    {
        _rate = rate; _ch = channels;
        _n = 1; while (_n < rate / 6) _n <<= 1;        // okno približno 0,17 s
        _hop = _n / 4;
        _l = new float[_n]; _r = new float[_n];
        _win = new double[_n];
        double s = 0;
        for (int i = 0; i < _n; i++) { _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (_n - 1)); s += _win[i]; }
        _sw = s;
        _re1 = new double[_n]; _im1 = new double[_n]; _re2 = new double[_n]; _im2 = new double[_n];
    }

    /// <summary>Zadnji rezultat (varno iz drugega niza).</summary>
    public AzimuthResult Latest { get { lock (_lock) return _last; } }

    public void Reset()
    {
        lock (_lock) { _last = new AzimuthResult(); }
        _have = false;
    }

    /// <summary>Sprejme prepleteno zaporedje vzorcev (n vzorcev × ch kanalov); uporablja prva dva kanala.</summary>
    public void Process(float[] buf, int n, int ch)
    {
        if (ch < 2) return;
        for (int i = 0; i < n; i++)
        {
            float a = buf[i * ch], b = buf[i * ch + 1];
            _l[_pos] = a; _r[_pos] = b;
            float aa = Math.Abs(a), ab = Math.Abs(b);
            if (aa > _pkL) _pkL = aa;
            if (ab > _pkR) _pkR = ab;
            if (++_pos >= _n) _pos = 0;
            if (_filled < _n) _filled++;
            if (++_since >= _hop && _filled >= _n) { _since = 0; Analyze(); }
        }
    }

    private static double Db(double a) => 20 * Math.Log10(Math.Max(a, 1e-9));

    private void Analyze()
    {
        int n = _n;
        for (int j = 0; j < n; j++)
        {
            int idx = _pos + j; if (idx >= n) idx -= n;
            double w = _win[j];
            _re1[j] = _l[idx] * w; _im1[j] = 0;
            _re2[j] = _r[idx] * w; _im2[j] = 0;
        }
        Spectrum.Fft(_re1, _im1);
        Spectrum.Fft(_re2, _im2);

        double binHz = (double)_rate / n;
        double f0 = NominalHz;
        double lo = f0 > 0 ? f0 * 0.92 : 5000, hi = f0 > 0 ? f0 * 1.08 : 18000;
        hi = Math.Min(hi, _rate * 0.47);
        int kLo = Math.Max(8, (int)Math.Ceiling(lo / binHz)), kHi = Math.Min(n / 2 - 8, (int)Math.Floor(hi / binHz));

        double pkL = _pkL, pkR = _pkR; _pkL = 0; _pkR = 0;
        var bad = new AzimuthResult { Valid = false, PeakLDb = Db(pkL), PeakRDb = Db(pkR) };
        if (kHi <= kLo + 8) { Publish(bad, false); return; }

        // najmočnejši vrh vsote moči obeh kanalov
        int kMax = kLo; double pMax = -1;
        for (int k = kLo; k <= kHi; k++)
        {
            double p = _re1[k] * _re1[k] + _im1[k] * _im1[k] + _re2[k] * _re2[k] + _im2[k] * _im2[k];
            if (p > pMax) { pMax = p; kMax = k; }
        }
        // šum: povprečje moči zunaj ±6 binov od vrha
        double pNoise = 0; int cn = 0;
        for (int k = kLo; k <= kHi; k++)
            if (Math.Abs(k - kMax) > 6) { pNoise += _re1[k] * _re1[k] + _im1[k] * _im1[k] + _re2[k] * _re2[k] + _im2[k] * _im2[k]; cn++; }
        pNoise = cn > 0 ? pNoise / cn : 1e-30;

        // navzkrižni spekter in energije v okolici vrha (±2 bina, glavni snop Hannovega okna)
        double cre = 0, cim = 0, eL = 0, eR = 0, eS = 0;
        for (int k = kMax - 2; k <= kMax + 2; k++)
        {
            double lr = _re1[k], li = _im1[k], rr = _re2[k], ri = _im2[k];
            cre += lr * rr + li * ri;          // Re(L · conj(R))
            cim += li * rr - lr * ri;          // Im(L · conj(R))
            eL += lr * lr + li * li;
            eR += rr * rr + ri * ri;
            double sr = lr + rr, si = li + ri;
            eS += sr * sr + si * si;
        }
        double ampK = 32.0 / (3.0 * n * (double)n);
        double aL = Math.Sqrt(eL * ampK), aR = Math.Sqrt(eR * ampK), aS = Math.Sqrt(eS * ampK);

        // frekvenca z interpolacijo vrha
        double fHz = kMax * binHz;
        {
            double a = Math.Log(Math.Max(P(kMax - 1), 1e-30)), b = Math.Log(Math.Max(P(kMax), 1e-30)), c = Math.Log(Math.Max(P(kMax + 1), 1e-30));
            double den = a - 2 * b + c;
            if (Math.Abs(den) > 1e-12) fHz = (kMax + Math.Max(-0.5, Math.Min(0.5, 0.5 * (a - c) / den))) * binHz;
        }

        bool tone = aL > 1e-3 && aR > 1e-3 && (eL + eR) > 100 * pNoise * 5;     // ton vsaj ~20 dB nad šumom v obeh kanalih
        if (!tone) { Publish(bad, false); return; }

        double mag = Math.Sqrt(cre * cre + cim * cim);
        if (mag <= 0) { Publish(bad, false); return; }
        double ux = cre / mag, uy = cim / mag;
        double dbL = Db(aL), dbR = Db(aR), dbS = Db(aS);
        if (!_have) { _ux = ux; _uy = uy; _dbL = dbL; _dbR = dbR; _dbS = dbS; _fHz = fHz; _have = true; }
        else
        {
            _ux = 0.6 * _ux + 0.4 * ux; _uy = 0.6 * _uy + 0.4 * uy;
            _dbL = 0.6 * _dbL + 0.4 * dbL; _dbR = 0.6 * _dbR + 0.4 * dbR; _dbS = 0.6 * _dbS + 0.4 * dbS;
            _fHz = 0.6 * _fHz + 0.4 * fHz;
        }
        double ph = Math.Atan2(_uy, _ux) * 180 / Math.PI;
        var res = new AzimuthResult
        {
            Valid = true, FreqHz = _fHz, LevelLDb = _dbL, LevelRDb = _dbR, SumDb = _dbS,
            PhaseDeg = ph, DelayUs = ph / (360.0 * _fHz) * 1e6, PeakLDb = Db(pkL), PeakRDb = Db(pkR),
        };
        Publish(res, true);

        double P(int k) => _re1[k] * _re1[k] + _im1[k] * _im1[k] + _re2[k] * _re2[k] + _im2[k] * _im2[k];
    }

    private void Publish(AzimuthResult r, bool valid)
    {
        if (!valid) _have = false;
        lock (_lock) _last = r;
    }

    /// <summary>Kopira zadnjih (največ l.Length) vzorcev L in D v tabeli (najstarejši prvi). Vrne število vzorcev. Samo za prikaz (brez zaklepanja).</summary>
    public int Snapshot(float[] l, float[] r)
    {
        int cnt = Math.Min(Math.Min(l.Length, r.Length), Math.Min(_filled, _n));
        int p = _pos;
        for (int j = 0; j < cnt; j++)
        {
            int idx = p - cnt + j; if (idx < 0) idx += _n;
            l[j] = _l[idx]; r[j] = _r[idx];
        }
        return cnt;
    }
}
