using System;
using System.Collections.Generic;
using System.Windows;

namespace CDSMeasure.Core;

/// <summary>
/// Amplitudni spekter odstopanja hitrosti (Welch: Hannovo okno, 50 % prekrivanje).
/// Izhod: (frekvenca Hz, amplituda vrha v %). Pri 200 Hz in 16 s oknu je ločljivost 0,0625 Hz.
/// </summary>
public static class Spectrum
{
    public static List<Point> Welch(IList<float> x, double fs, double segSec = 16.0, double overlap = 0.5,
                                    double fMin = 0.2, double fMax = 100.0, int maxSegments = 8)
    {
        var res = new List<Point>();
        int total = x.Count;
        int seg = (int)Math.Min(segSec * fs, total);
        if (seg < (int)(2 * fs)) return res;                 // premalo podatkov

        int nfft = 1; while (nfft < seg) nfft <<= 1;
        var win = new double[seg];
        double sw = 0;
        for (int i = 0; i < seg; i++) { win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (seg - 1)); sw += win[i]; }

        int hop = Math.Max(1, (int)(seg * (1 - overlap)));
        int nSeg = Math.Min(maxSegments, (total - seg) / hop + 1);
        int first = total - seg - (nSeg - 1) * hop;           // zadnji segmenti (sveži podatki)
        int half = nfft / 2;
        var pwr = new double[half + 1];
        var re = new double[nfft]; var im = new double[nfft];

        for (int s = 0; s < nSeg; s++)
        {
            int o = first + s * hop;
            double mean = 0;
            for (int i = 0; i < seg; i++) mean += x[o + i];
            mean /= seg;
            Array.Clear(re, 0, re.Length); Array.Clear(im, 0, im.Length);
            for (int i = 0; i < seg; i++) re[i] = (x[o + i] - mean) * win[i];
            Fft(re, im);
            for (int k = 0; k <= half; k++)
            {
                double a = 2.0 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / sw;   // amplituda vrha sinusa
                pwr[k] += a * a;
            }
        }

        for (int k = 1; k <= half; k++)
        {
            double f = k * fs / nfft;
            if (f < fMin || f > fMax) continue;
            res.Add(new Point(f, Math.Sqrt(pwr[k] / nSeg)));
        }
        return res;
    }

    /// <summary>Radix-2 FFT na mestu (dolžina mora biti potenca števila 2).</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }
}
