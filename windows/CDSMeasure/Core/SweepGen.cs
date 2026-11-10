using System.Runtime.InteropServices;
using NAudio.Wave;

namespace CDSMeasure.Core;

/// <summary>Generator logaritemskega sweepa (stereo, isti signal na obeh kanalih) za ASIO izhod.</summary>
public sealed class SweepGen : IWaveProvider
{
    private readonly double _f0, _ratio, _dur, _amp;
    private readonly int _rate;
    private long _n;
    private readonly double _lnr, _k;

    public WaveFormat WaveFormat { get; }
    /// <summary>Trajanje sweepa v sekundah.</summary>
    public double Seconds => _dur;
    /// <summary>Sweep je odvrtel do konca (nato generator oddaja tišino).</summary>
    public bool Finished => _n >= (long)(_dur * _rate);

    /// <param name="levelDbfs">Amplituda vrha sweepa v dBFS.</param>
    public SweepGen(int rate, double f0, double f1, double seconds, double levelDbfs)
    {
        _rate = rate; _f0 = f0; _ratio = f1 / f0; _dur = seconds;
        _lnr = Math.Log(_ratio); _k = 2 * Math.PI * f0 * seconds / _lnr;
        _amp = Math.Pow(10, levelDbfs / 20);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var f = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
        long total = (long)(_dur * _rate);
        for (int i = 0; i + 1 < f.Length; i += 2)
        {
            float s = 0;
            if (_n < total)
            {
                double t = (double)_n / _rate;
                double phase = _k * (Math.Exp(t * _lnr / _dur) - 1);   // točna faza logaritemskega sweepa
                double env = Math.Min(1.0, Math.Min(t / 0.05, (_dur - t) / 0.05));   // 50 ms vstop/izstop
                s = (float)(_amp * Math.Max(0, env) * Math.Sin(phase));
                _n++;
            }
            f[i] = s; f[i + 1] = s;
        }
        return count;
    }
}
