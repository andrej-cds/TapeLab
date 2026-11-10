using System;
using System.Linq;

namespace CDSMeasure.Core;

/// <summary>Mreža 1/24 oktave (10 Hz …) za izvoz CSV pri meritvi frekvenčnega odziva snemanja.</summary>
public static class RecAnalysis
{
    public static readonly double[] Grid = Enumerable.Range(0, 277).Select(i => 10.0 * Math.Pow(2, i / 24.0)).ToArray();
}
