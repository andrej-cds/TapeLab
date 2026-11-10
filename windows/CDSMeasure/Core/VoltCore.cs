using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CDSMeasure.Core;

/// <summary>Merilno območje voltmetra. MarkV = nazivna (merjena) napetost na sredini traku; NaN = standardna skala brez oznake.</summary>
public sealed record VoltRange(string Name, double MaxV, double MarkV, double StepV, int MinorPerMajor)
{
    public bool HasMark => !double.IsNaN(MarkV);
    /// <summary>Skala se izpisuje v mV, kadar je maksimum pod 1 V.</summary>
    public bool InMilli => MaxV < 1.0;
    public string Unit => InMilli ? "mV" : "V";
    public double Div => InMilli ? 1e-3 : 1.0;

    public static string Format(double volts)
    {
        double a = Math.Abs(volts);
        var inv = CultureInfo.InvariantCulture;
        if (a >= 1.0) return volts.ToString(a >= 10 ? "0.0" : "0.00", inv) + " V";
        if (a >= 0.1) return (volts * 1e3).ToString("0.0", inv) + " mV";
        if (a >= 0.01) return (volts * 1e3).ToString("0.00", inv) + " mV";
        return (volts * 1e3).ToString("0.000", inv) + " mV";
    }
}

public static class VoltRanges
{
    /// <summary>Vrstni red po nazivni napetosti. Merjene (simetrične) skale: 9, 90, 200, 500 mV s sredino na merjeni vrednosti; ostale 1 mV … 10 V od 0 do oznake.</summary>
    public static readonly VoltRange[] All =
    {
        new("1 mV",   0.001, double.NaN, 0.0001, 5),
        new("9 mV",   0.018, 0.009,      0.001,  2),
        new("10 mV",  0.010, double.NaN, 0.001,  5),
        new("90 mV",  0.180, 0.090,      0.010,  2),
        new("100 mV", 0.100, double.NaN, 0.010,  5),
        new("200 mV", 0.400, 0.200,      0.020,  2),
        new("500 mV", 1.000, 0.500,      0.050,  2),
        new("1 V",    1.000, double.NaN, 0.100,  5),
        new("10 V",   10.0,  double.NaN, 1.0,    5),
    };

    public const int DefaultIndex = 3;   // 90 mV
}

/// <summary>Meritev AC True RMS po kanalih (brez enosmerne komponente), okno 200 ms. Vrednosti so v deležu polne skale (1,0 = 0 dBFS, sinus: 0,707 = −3 dBFS RMS).</summary>
public sealed class VoltCore
{
    private readonly int _win;
    private readonly int _ch;
    private readonly double[] _sum = new double[2], _sq = new double[2];
    private readonly double[] _peak = new double[2];
    private int _count;

    private readonly double[] _rms = new double[2], _pk = new double[2];
    private volatile bool _valid;

    public VoltCore(int sampleRate, int channels)
    {
        _win = Math.Max(256, sampleRate / 5);
        _ch = Math.Clamp(channels, 1, 2);
    }

    public int Channels => _ch;
    public bool Valid => _valid;
    public double Rms(int ch) { lock (_rms) return _rms[ch]; }
    public double Peak(int ch) { lock (_rms) return _pk[ch]; }

    /// <summary>buf = prepleteni vzorci (n vzorcev × c kanalov).</summary>
    public void Process(float[] buf, int n, int c)
    {
        int use = Math.Min(c, _ch);
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < use; k++)
            {
                double x = buf[i * c + k];
                _sum[k] += x; _sq[k] += x * x;
                double a = Math.Abs(x);
                if (a > _peak[k]) _peak[k] = a;
            }
            if (++_count >= _win) Flush();
        }
    }

    private void Flush()
    {
        lock (_rms)
        {
            for (int k = 0; k < _ch; k++)
            {
                double mean = _sum[k] / _count;
                double v = _sq[k] / _count - mean * mean;
                _rms[k] = v > 0 ? Math.Sqrt(v) : 0;
                _pk[k] = _peak[k];
                _sum[k] = 0; _sq[k] = 0; _peak[k] = 0;
            }
        }
        _count = 0;
        _valid = true;
    }
}

/// <summary>Umeritev: napetost (V RMS) pri polni skali ADC-ja, ločeno za kanala. Velja samo za nastavljen gain na kartici; shranjeno v %AppData%\CDSTapeLab\volt.json.</summary>
public static class VoltCal
{
    private sealed class Data
    {
        public double[] VoltsPerFs { get; set; } = { 1.0, 1.0 };
        public bool[] Calibrated { get; set; } = { false, false };
    }

    private static Data _d = new();
    private static string PathFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDSTapeLab", "volt.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(PathFile));
                if (d is { VoltsPerFs.Length: >= 2, Calibrated.Length: >= 2 }) _d = d;
            }
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(_d));
        }
        catch { }
    }

    public static bool IsCalibrated(int ch) => _d.Calibrated[ch];
    public static double VoltsPerFs(int ch) => _d.VoltsPerFs[ch];

    /// <summary>Volti (RMS) za izmerjeni delež polne skale.</summary>
    public static double ToVolts(int ch, double fsRms) => fsRms * _d.VoltsPerFs[ch];

    /// <summary>Nastavi umeritev tako, da trenutni delež polne skale ustreza referenčni napetosti.</summary>
    public static bool Set(int ch, double fsRms, double refVolts)
    {
        if (fsRms < 1e-7 || refVolts <= 0) return false;
        _d.VoltsPerFs[ch] = refVolts / fsRms;
        _d.Calibrated[ch] = true;
        Save();
        return true;
    }

    public static void Reset()
    {
        _d = new Data();
        Save();
    }
}
