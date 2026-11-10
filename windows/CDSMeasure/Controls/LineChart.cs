using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CDSMeasure.Controls;

/// <summary>Črtni graf z linearno ali logaritemsko X osjo (čas, spekter).</summary>
public class LineChart : FrameworkElement
{
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Mono = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush BgBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C)));
    private static readonly Brush LightBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xBC, 0x85)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xA9, 0x6E))), 1));
    private static readonly Pen ZeroPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xA0, 0xC8, 0xA9, 0x6E))), 1.2));
    private static readonly Pen FramePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x22))), 1));
    private static readonly Pen LinePen = Frozen(new Pen(LightBrush, 1.4) { LineJoin = PenLineJoin.Round });

    private IReadOnlyList<Point>? _pts;
    private double _x0, _x1 = 1, _y0, _y1 = 1;

    public bool LogX { get; set; }
    public double[]? XTicks { get; set; }
    public string XFormat { get; set; } = "0.##";
    public string YFormat { get; set; } = "0.00";
    public string XUnit { get; set; } = "";
    public string YUnit { get; set; } = "";

    /// <summary>Navpične oznake (npr. pričakovane frekvence mehanskih delov): X in besedilo.</summary>
    public IReadOnlyList<(double X, string Label)>? Markers { get; set; }
    private static readonly Pen MarkPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xE0, 0x9A, 0x3C))), 1) { DashStyle = DashStyles.Dash });

    public void Refresh() => InvalidateVisual();

    public void SetData(IReadOnlyList<Point>? pts, double x0, double x1, double y0, double y1)
    {
        _pts = pts; _x0 = x0; _x1 = x1; _y0 = y0; _y1 = y1;
        InvalidateVisual();
    }

    private static List<double> NiceTicks(double lo, double hi, int target)
    {
        var res = new List<double>();
        double range = hi - lo;
        if (range <= 0) return res;
        double rough = range / target;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double step = mag;
        foreach (double s in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
        {
            step = s * mag;
            if (step >= rough) break;
        }
        double start = Math.Ceiling(lo / step - 1e-9) * step;
        for (double v = start; v <= hi + step * 1e-6; v += step)
            res.Add(Math.Round(v / step) * step);
        return res;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 120 || h < 70) return;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double L = 62, T = 10, R = w - 14, B = h - 28;
        var plot = new Rect(L, T, R - L, B - T);
        dc.DrawRectangle(BgBrush, FramePen, plot);

        double lx0 = LogX ? Math.Log10(Math.Max(_x0, 1e-9)) : _x0;
        double lx1 = LogX ? Math.Log10(Math.Max(_x1, 1e-9)) : _x1;
        if (lx1 <= lx0 || _y1 <= _y0) return;

        double X(double v)
        {
            double a = LogX ? Math.Log10(Math.Max(v, 1e-9)) : v;
            return L + (a - lx0) / (lx1 - lx0) * plot.Width;
        }
        double Y(double v) => B - (v - _y0) / (_y1 - _y0) * plot.Height;

        FormattedText Text(string s, Brush b, double size = 11) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, size, b, ppd);

        // Y mreža
        foreach (double v in NiceTicks(_y0, _y1, 6))
        {
            double y = Y(v);
            dc.DrawLine(Math.Abs(v) < 1e-12 ? ZeroPen : GridPen, new Point(L, y), new Point(R, y));
            var ft = Text((Math.Abs(v) < 1e-12 ? 0.0 : v).ToString(YFormat, CultureInfo.CurrentCulture), DimBrush);   // brez »-0,000«
            dc.DrawText(ft, new Point(L - 8 - ft.Width, y - ft.Height / 2));
        }

        // X mreža
        IEnumerable<double> xt = XTicks ?? NiceTicks(_x0, _x1, 8).ToArray();
        foreach (double v in xt)
        {
            if (v < _x0 || v > _x1) continue;
            double x = X(v);
            dc.DrawLine(GridPen, new Point(x, T), new Point(x, B));
            var ft = Text(v.ToString(XFormat, CultureInfo.CurrentCulture), DimBrush);
            dc.DrawText(ft, new Point(x - ft.Width / 2, B + 6));
        }

        if (YUnit.Length > 0) dc.DrawText(Text(YUnit, LightBrush), new Point(L + 6, T + 3));
        if (XUnit.Length > 0)
        {
            var fu = Text(XUnit, LightBrush);
            dc.DrawText(fu, new Point(R - fu.Width - 6, B - fu.Height - 3));
        }

        // oznake mehanike
        if (Markers is { Count: > 0 })
        {
            dc.PushClip(new RectangleGeometry(plot));
            int row = 0;
            foreach (var m in Markers.OrderBy(q => q.X))
            {
                if (m.X < _x0 || m.X > _x1) continue;
                double mx = X(m.X);
                dc.DrawLine(MarkPen, new Point(mx, T), new Point(mx, B));
                var ft = Text(m.Label, LightBrush, 10);
                double tx = Math.Min(mx + 4, R - ft.Width - 2);
                dc.DrawText(ft, new Point(tx, T + 3 + (row++ % 4) * 13));
            }
            dc.Pop();
        }

        // podatki
        var pts = _pts;
        if (pts == null || pts.Count < 2) return;

        dc.PushClip(new RectangleGeometry(plot));
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            int n = pts.Count;
            if (n <= plot.Width * 2)
            {
                bool pen = false;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    if (double.IsNaN(p.Y) || double.IsInfinity(p.Y)) { pen = false; continue; }
                    var q = new Point(X(p.X), Y(p.Y));
                    if (!pen) { c.BeginFigure(q, false, false); pen = true; }
                    else c.LineTo(q, true, false);
                }
            }
            else
            {
                // zgostitev: min/max na slikovno točko (ohrani vrhove)
                int cols = (int)plot.Width;
                var mn = new double[cols]; var mx = new double[cols];
                Array.Fill(mn, double.PositiveInfinity); Array.Fill(mx, double.NegativeInfinity);
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    if (double.IsNaN(p.Y)) continue;
                    int k = (int)(X(p.X) - L);
                    if (k < 0 || k >= cols) continue;
                    if (p.Y < mn[k]) mn[k] = p.Y;
                    if (p.Y > mx[k]) mx[k] = p.Y;
                }
                bool started = false;
                for (int k = 0; k < cols; k++)
                {
                    if (double.IsInfinity(mn[k])) continue;
                    var a = new Point(L + k + 0.5, Y(mn[k]));
                    var b = new Point(L + k + 0.5, Y(mx[k]));
                    if (!started) { c.BeginFigure(a, false, false); started = true; }
                    else c.LineTo(a, true, false);
                    c.LineTo(b, true, false);
                }
            }
        }
        sg.Freeze();
        dc.DrawGeometry(null, LinePen, sg);
        dc.Pop();
    }
}
