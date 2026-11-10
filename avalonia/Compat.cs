namespace System.Windows
{
    /// <summary>Nadomestek za WPF Point, da se skupno jedro (Spectrum, SweepGen ...) prevede tudi brez WPF.</summary>
    public readonly struct Point
    {
        public double X { get; }
        public double Y { get; }
        public Point(double x, double y) { X = x; Y = y; }
    }
}
