using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CDSMeasure.Controls;

/// <summary>Črtni graf z linearno ali logaritemsko X osjo (čas, spekter).</summary>
public sealed class LineChart : Control
{
    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    private static readonly IBrush BgBrush = Brush.Parse("#0A0A0A");
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xA9, 0x6E)), 1);
    private static readonly Pen ZeroPen = new(new SolidColorBrush(Color.FromArgb(0xA0, 0xC8, 0xA9, 0x6E)), 1.2);
    private static readonly Pen FramePen = new(Ui.Line, 1);
    private static readonly Pen LinePen = new(Ui.GoldLight, 1.4, lineJoin: PenLineJoin.Round);

    private IReadOnlyList<System.Windows.Point>? _pts;
    private double _x0, _x1 = 1, _y0, _y1 = 1;

    public bool LogX { get; set; }
    public double[]? XTicks { get; set; }
    public string XFormat { get; set; } = "0.##";
    public string YFormat { get; set; } = "0.00";
    public string XUnit { get; set; } = "";
    public string YUnit { get; set; } = "";

    public void SetData(IReadOnlyList<System.Windows.Point>? pts, double x0, double x1, double y0, double y1)
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

    private static FormattedText Text(string s, IBrush b, double size = 11) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 120 || h < 70) return;
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
        var inv = CultureInfo.InvariantCulture;

        foreach (double v in NiceTicks(_y0, _y1, 6))
        {
            double y = Y(v);
            dc.DrawLine(Math.Abs(v) < 1e-12 ? ZeroPen : GridPen, new Point(L, y), new Point(R, y));
            var ft = Text((Math.Abs(v) < 1e-12 ? 0.0 : v).ToString(YFormat, inv), Ui.Dim);
            dc.DrawText(ft, new Point(L - 8 - ft.Width, y - ft.Height / 2));
        }
        IEnumerable<double> xt = XTicks ?? NiceTicks(_x0, _x1, 8).ToArray();
        foreach (double v in xt)
        {
            if (v < _x0 || v > _x1) continue;
            double x = X(v);
            dc.DrawLine(GridPen, new Point(x, T), new Point(x, B));
            var ft = Text(v.ToString(XFormat, inv), Ui.Dim);
            dc.DrawText(ft, new Point(x - ft.Width / 2, B + 6));
        }
        if (YUnit.Length > 0) dc.DrawText(Text(YUnit, Ui.GoldLight), new Point(L + 6, T + 3));
        if (XUnit.Length > 0)
        {
            var fu = Text(XUnit, Ui.GoldLight);
            dc.DrawText(fu, new Point(R - fu.Width - 6, B - fu.Height - 3));
        }

        var pts = _pts;
        if (pts == null || pts.Count < 2) return;
        using var clip = dc.PushClip(plot);
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            int n = pts.Count;
            bool open = false;
            if (n <= plot.Width * 2)
            {
                bool pen = false;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    if (double.IsNaN(p.Y) || double.IsInfinity(p.Y)) { pen = false; continue; }
                    var q = new Point(X(p.X), Y(p.Y));
                    if (!pen) { if (open) c.EndFigure(false); c.BeginFigure(q, false); open = true; pen = true; }
                    else c.LineTo(q);
                }
            }
            else
            {
                int cols = Math.Max(1, (int)plot.Width);
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
                    if (!started) { c.BeginFigure(a, false); started = true; open = true; }
                    else c.LineTo(a);
                    c.LineTo(b);
                }
            }
            if (open) c.EndFigure(false);
        }
        dc.DrawGeometry(null, LinePen, sg);
    }
}
