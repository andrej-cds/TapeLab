using System.Runtime.InteropServices;
using PortAudioSharp;

namespace CDSMeasure.Audio;

public sealed record AudioDevice(int Index, string Name, int MaxIn, double DefaultRate);

/// <summary>Seznam vhodnih naprav prek PortAudio (CoreAudio na macOS, ALSA/PipeWire na Linuxu).</summary>
public static class PaEngine
{
    private static bool _ready;
    public static string LastError { get; private set; } = "";

    public static string ApiName =>
        OperatingSystem.IsMacOS() ? "CoreAudio" : OperatingSystem.IsLinux() ? "ALSA" : "PortAudio";

    public static bool Init()
    {
        if (_ready) return true;
        try { PortAudio.Initialize(); _ready = true; LastError = ""; }
        catch (Exception ex) { LastError = ex.Message; }
        return _ready;
    }

    public static List<AudioDevice> Inputs()
    {
        var list = new List<AudioDevice>();
        if (!Init()) return list;
        try
        {
            int n = PortAudio.DeviceCount;
            for (int i = 0; i < n; i++)
            {
                var di = PortAudio.GetDeviceInfo(i);
                if (di.maxInputChannels > 0)
                    list.Add(new AudioDevice(i, di.name ?? ("#" + i), di.maxInputChannels, di.defaultSampleRate));
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
        return list;
    }

    /// <summary>Naprava iz nastavitev; če ni izbrana ali je ni več: Behringer / UMC, sicer privzeti vhod, sicer prva.</summary>
    public static AudioDevice? Resolve()
    {
        var all = Inputs();
        if (all.Count == 0) return null;
        if (!string.IsNullOrEmpty(AppSettings.AsioDriver))
        {
            var d = all.FirstOrDefault(x => x.Name == AppSettings.AsioDriver);
            if (d != null) return d;
        }
        var umc = all.FirstOrDefault(x => x.Name.Contains("UMC", StringComparison.OrdinalIgnoreCase)
                                       || x.Name.Contains("Behringer", StringComparison.OrdinalIgnoreCase));
        if (umc != null) return umc;
        try
        {
            int def = PortAudio.DefaultInputDevice;
            var d = all.FirstOrDefault(x => x.Index == def);
            if (d != null) return d;
        }
        catch { }
        return all[0];
    }

    /// <summary>Napis na dnu začetnega zaslona.</summary>
    public static string CardText()
    {
        var d = Resolve();
        string rate = AppSettings.SampleRate > 0
            ? (AppSettings.SampleRate / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz"
            : Ui.Tt("samodejno", "auto");
        return (d?.Name ?? "—") + " · " + ApiName + " · " + rate;
    }
}

/// <summary>Zajem iz izbranega para vhodnih kanalov in pošiljanje prepletenih vzorcev (float) v jedro meritve.</summary>
public sealed class PaCapture : IDisposable
{
    private PortAudioSharp.Stream? _stream;
    private PortAudioSharp.Stream.Callback? _cb;     // referenca, da je GC ne pobere
    private float[] _tmp = Array.Empty<float>();

    public string DeviceName { get; private set; } = "";
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }

    private static int[] Rates(AudioDevice dev)
    {
        if (AppSettings.SampleRate > 0) return new[] { AppSettings.SampleRate };
        var r = new List<int> { 192000, 96000, 48000 };
        int def = (int)Math.Round(dev.DefaultRate);
        if (def > 0 && !r.Contains(def)) r.Add(def);
        return r.ToArray();
    }

    /// <summary>Zažene zajem. firstCh = prvi vhodni kanal (od 0), count = število kanalov (1–2 za meritve). Vrne napako ali null.</summary>
    public string? Start(AudioDevice dev, int firstCh, int count, Action<float[], int, int> sink)
    {
        Stop();
        if (!PaEngine.Init()) return PaEngine.LastError;
        count = Math.Max(1, Math.Min(count, dev.MaxIn));
        if (firstCh < 0 || firstCh + count > dev.MaxIn) firstCh = 0;
        int openCh = firstCh + count;
        int first = firstCh, cnt = count;
        string last = "";

        foreach (int rate in Rates(dev))
        {
            try
            {
                var inp = new StreamParameters
                {
                    device = dev.Index,
                    channelCount = openCh,
                    sampleFormat = SampleFormat.Float32,
                    suggestedLatency = PortAudio.GetDeviceInfo(dev.Index).defaultLowInputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero,
                };
                _cb = (IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo ti, StreamCallbackFlags flags, IntPtr user) =>
                {
                    int n = (int)frames;
                    if (input != IntPtr.Zero && n > 0)
                    {
                        int total = n * openCh;
                        if (_tmp.Length < total) _tmp = new float[total];
                        Marshal.Copy(input, _tmp, 0, total);
                        var buf = new float[n * cnt];
                        for (int i = 0; i < n; i++)
                            for (int k = 0; k < cnt; k++)
                                buf[i * cnt + k] = _tmp[i * openCh + first + k];
                        sink(buf, n, cnt);
                    }
                    return StreamCallbackResult.Continue;
                };
                var s = new PortAudioSharp.Stream(inParams: inp, outParams: null, sampleRate: rate, framesPerBuffer: 0,
                    streamFlags: StreamFlags.ClipOff, callback: _cb, userData: IntPtr.Zero);
                s.Start();
                _stream = s;
                DeviceName = dev.Name; SampleRate = rate; Channels = cnt;
                return null;
            }
            catch (Exception ex) { last = ex.Message; _stream = null; }
        }
        return last;
    }

    public void Stop()
    {
        var s = _stream;
        _stream = null;
        if (s == null) return;
        try { s.Stop(); } catch { }
        try { s.Close(); } catch { }
        try { s.Dispose(); } catch { }
    }

    public void Dispose() => Stop();
}
