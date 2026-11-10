// Združljivost z .NET Framework 4.0 (Windows XP): globalni using-i in majhni nadomestki za novejše tipe.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace System.Runtime.CompilerServices
{
    // omogoča "init" in "record" v kodi
    internal static class IsExternalInit { }

    // imena elementov terk (npr. (string Key, object Val))
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field |
                    AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string?[] transformNames) { TransformNames = transformNames; }
        public string?[] TransformNames { get; }
    }
}

namespace System
{
    // C# terke (tuple) – tipa v .NET 4.0 ni
    public struct ValueTuple<T1, T2>
    {
        public T1 Item1; public T2 Item2;
        public ValueTuple(T1 i1, T2 i2) { Item1 = i1; Item2 = i2; }
    }
    public struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1; public T2 Item2; public T3 Item3;
        public ValueTuple(T1 i1, T2 i2, T3 i3) { Item1 = i1; Item2 = i2; Item3 = i3; }
    }
    public struct ValueTuple<T1, T2, T3, T4>
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4;
        public ValueTuple(T1 i1, T2 i2, T3 i3, T4 i4) { Item1 = i1; Item2 = i2; Item3 = i3; Item4 = i4; }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5>
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5;
        public ValueTuple(T1 i1, T2 i2, T3 i3, T4 i4, T5 i5) { Item1 = i1; Item2 = i2; Item3 = i3; Item4 = i4; Item5 = i5; }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7>
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6; public T7 Item7;
        public ValueTuple(T1 i1, T2 i2, T3 i3, T4 i4, T5 i5, T6 i6, T7 i7) { Item1 = i1; Item2 = i2; Item3 = i3; Item4 = i4; Item5 = i5; Item6 = i6; Item7 = i7; }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6>
    {
        public T1 Item1; public T2 Item2; public T3 Item3; public T4 Item4; public T5 Item5; public T6 Item6;
        public ValueTuple(T1 i1, T2 i2, T3 i3, T4 i4, T5 i5, T6 i6) { Item1 = i1; Item2 = i2; Item3 = i3; Item4 = i4; Item5 = i5; Item6 = i6; }
    }
}

namespace CDSMeasure
{
    internal static class ArrayX
    {
        public static void Fill<T>(T[] arr, T value) { for (int i = 0; i < arr.Length; i++) arr[i] = value; }
    }

    internal static class MathX
    {
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Nadomestek za VisualTreeHelper.GetDpi (v .NET 4.0 ga ni).</summary>
    internal struct DpiInfo
    {
        public double DpiScaleX, DpiScaleY;
        public double PixelsPerDip => DpiScaleY;
    }

    internal static class Dpi
    {
        public static DpiInfo Get(Visual v)
        {
            double sx = 1, sy = 1;
            try
            {
                var src = PresentationSource.FromVisual(v);
                if (src != null && src.CompositionTarget != null)
                {
                    var m = src.CompositionTarget.TransformToDevice;
                    sx = m.M11; sy = m.M22;
                }
            }
            catch { }
            return new DpiInfo { DpiScaleX = sx, DpiScaleY = sy };
        }
    }

    /// <summary>FormattedText brez parametra pixelsPerDip (ta je šele od .NET 4.6.2).</summary>
    internal static class FT
    {
        public static FormattedText New(string text, CultureInfo culture, FlowDirection flow, Typeface face, double size, Brush brush, double ppd)
            => new FormattedText(text, culture, flow, face, size, brush);
    }
}
