using System;
using System.Linq;
using NAudio.Wave;

namespace CDSMeasure.Core;

/// <summary>Zajem iz zvočne kartice prek ASIO (npr. Behringer UMC202HD) in pošiljanje vzorcev v jedro meritve.</summary>
public sealed class AsioCapture : IDisposable
{
    private AsioOut? _asio;
    private WfCore? _core;
    private Action<float[], int, int>? _sink;   // surovi zajem (frekvenčni odziv)
    private int _channel;                 // 0 = L, 1 = D, 2 = L+D (povprečje obeh kanalov)

    public string DriverName { get; private set; } = "";
    public int SampleRate { get; private set; }
    public int InputChannels { get; private set; }

    public static string[] Drivers()
    {
        try { return AsioOut.GetDriverNames(); }
        catch { return new string[0]; }
    }

    /// <summary>Gonilnik iz nastavitev; če ni izbran ali ni nameščen, samodejno (Behringer / UMC, sicer prvi).</summary>
    public static string? FindDefault()
    {
        var names = Drivers();
        if (!string.IsNullOrEmpty(AppSettings.AsioDriver) && names.Contains(AppSettings.AsioDriver)) return AppSettings.AsioDriver;
        return FindAuto(names);
    }

    /// <summary>Samodejna izbira: Behringer / UMC, sicer prvi ASIO gonilnik.</summary>
    public static string? FindAuto() => FindAuto(Drivers());

    private static string? FindAuto(string[] names) =>
        names.FirstOrDefault(n => n.IndexOf("UMC", StringComparison.OrdinalIgnoreCase) >= 0
                               || n.IndexOf("Behringer", StringComparison.OrdinalIgnoreCase) >= 0)
        ?? names.FirstOrDefault();

    private static int _open;
    /// <summary>Res, dokler teče kateri koli zajem (gonilnika takrat ne smemo odpirati še enkrat).</summary>
    public static bool IsActive => _open > 0;

    private static int[] Rates() =>
        AppSettings.SampleRate > 0 ? new[] { AppSettings.SampleRate } : new[] { 192000, 96000, 48000 };

    /// <summary>Nastavi vhodni in izhodni par kanalov iz nastavitev (če par ne obstaja, prvi par). Vrne število vhodnih kanalov (1–2).</summary>
    private static int Prepare(AsioOut asio)
    {
        int inCnt = asio.DriverInputChannelCount, outCnt = asio.DriverOutputChannelCount;
        int ch = Math.Max(1, Math.Min(2, inCnt));
        int io = AppSettings.InputStart, oo = AppSettings.OutputStart;
        if (io < 0 || io + ch > inCnt) io = 0;
        if (oo < 0 || oo + 2 > outCnt) oo = 0;
        asio.InputChannelOffset = io;
        asio.ChannelOffset = oo;
        return ch;
    }

    /// <summary>Zažene zajem. Vrne napako ali null, če je uspelo. Poskusi 192, 96 in 48 kHz.</summary>
    public string? Start(string driver, int channel, double refHz, out WfCore? core)
    {
        core = null;
        Stop();
        string lastError = "";
        foreach (int rate in Rates())
        {
            AsioOut? asio = null;
            try
            {
                asio = new AsioOut(driver);
                int ch = Prepare(asio);
                asio.InitRecordAndPlayback(null, ch, rate);
                _channel = channel >= 2 ? (ch >= 2 ? 2 : 0) : Math.Min(channel, ch - 1);
                _core = new WfCore(rate, refHz);
                asio.AudioAvailable += OnAudio;
                _asio = asio;
                asio.Play();
                DriverName = driver; SampleRate = rate; InputChannels = ch; System.Threading.Interlocked.Increment(ref _open); Diag.Log("ASIO zagnan: " + driver + ", " + rate + " Hz, vhodnih kanalov " + ch);
                core = _core;
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message; Diag.Log("ASIO napaka (" + driver + ", " + rate + " Hz): " + ex.Message);
                try { asio?.Dispose(); } catch { }
                _asio = null; _core = null;
            }
        }
        return lastError;
    }

    /// <summary>Hkrati predvaja signal (generator) in zajema vse vhodne kanale. Vrne napako ali null.</summary>
    public string? StartDuplex(string driver, Func<int, IWaveProvider> generator, Func<int, int, Action<float[], int, int>> sinkFactory)
    {
        Stop();
        string lastError = "";
        for (int attempt = 0; attempt < 2; attempt++)
        {
          if (attempt == 1) System.Threading.Thread.Sleep(600);      // gonilnik je morda še zaseden: poskusi znova
          foreach (int rate in Rates())
          {
            AsioOut? asio = null;
            try
            {
                var gen = generator(rate);
                asio = new AsioOut(driver);
                int ch = Prepare(asio);
                asio.InitRecordAndPlayback(gen, ch, rate);
                _sink = sinkFactory(rate, ch);
                asio.AudioAvailable += OnAudio;
                _asio = asio;
                asio.Play();
                DriverName = driver; SampleRate = rate; InputChannels = ch; System.Threading.Interlocked.Increment(ref _open); Diag.Log("ASIO zagnan: " + driver + ", " + rate + " Hz, vhodnih kanalov " + ch);
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message; Diag.Log("ASIO napaka (" + driver + ", " + rate + " Hz): " + ex.Message);
                try { asio?.Dispose(); } catch { }
                _asio = null; _sink = null;
            }
          }
        }
        return lastError;
    }

    /// <summary>Surov zajem vseh kanalov (za frekvenčni odziv). factory(rate, kanali) vrne sprejemnik vzorcev. Vrne napako ali null.</summary>
    public string? StartRaw(string driver, Func<int, int, Action<float[], int, int>> factory)
    {
        Stop();
        string lastError = "";
        foreach (int rate in Rates())
        {
            AsioOut? asio = null;
            try
            {
                asio = new AsioOut(driver);
                int ch = Prepare(asio);
                asio.InitRecordAndPlayback(null, ch, rate);
                _sink = factory(rate, ch);
                asio.AudioAvailable += OnAudio;
                _asio = asio;
                asio.Play();
                DriverName = driver; SampleRate = rate; InputChannels = ch; System.Threading.Interlocked.Increment(ref _open); Diag.Log("ASIO zagnan: " + driver + ", " + rate + " Hz, vhodnih kanalov " + ch);
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message; Diag.Log("ASIO napaka (" + driver + ", " + rate + " Hz): " + ex.Message);
                try { asio?.Dispose(); } catch { }
                _asio = null; _sink = null;
            }
        }
        return lastError;
    }

    private void OnAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        var sink = _sink;
        if (sink != null)
        {
            int c = e.InputBuffers.Length, m = e.SamplesPerBuffer;
            var b = new float[m * c];
            e.GetAsInterleavedSamples(b);
            sink(b, m, c);
            return;
        }
        var core = _core;
        if (core == null) return;
        int ch = e.InputBuffers.Length;
        int n = e.SamplesPerBuffer;
        var buf = new float[n * ch];
        e.GetAsInterleavedSamples(buf);
        if (_channel == 2 && ch >= 2)
        {
            // L+D: povprečje obeh kanalov
            var mono = new float[n];
            for (int i = 0; i < n; i++) mono[i] = 0.5f * (buf[i * ch] + buf[i * ch + 1]);
            core.Process(mono, n, 1, 0);
        }
        else
            core.Process(buf, n, ch, Math.Min(_channel, ch - 1));
    }

    public void Stop()
    {
        var a = _asio;
        _asio = null;
        if (a != null)
        {
            System.Threading.Interlocked.Decrement(ref _open);
            try { a.AudioAvailable -= OnAudio; a.Stop(); } catch { }
            try { a.Dispose(); } catch { }
            // gonilnik naj se popolnoma sprosti, preden ga naslednja meritev znova odpre
            GC.Collect(); GC.WaitForPendingFinalizers();
            System.Threading.Thread.Sleep(250);
        }
        _core = null; _sink = null;
    }

    public void Dispose() => Stop();
}
