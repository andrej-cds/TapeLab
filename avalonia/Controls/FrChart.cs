using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CDSMeasure.Controls;

/// <summary>Zvezni graf frekvenčnega odziva: logaritemska os 10 Hz – 30 kHz, L modro, D rdeče.</summary>
public sealed class FrChart : Control
{
    public sealed class Series
    {
        public double[] F = Array.Empty<double>();
        public double[]? FR;
        public double[]? L, R;
        public string Label = "";
        public Color? CL, CR;
    }

    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    private static readonly IBrush BgBrush = Brush.Parse("#0A0A0A");
    private static readonly Pen MinorPen = new(new SolidColorBrush(Color.FromArgb(0x24, 0xC8, 0xA9, 0x6E)), 1);
    private static readonly Pen MajorPen = new(new SolidColorBrush(Color.FromArgb(0x50, 0xC8, 0xA9, 0x6E)), 1);
    private static readonly Pen FramePen = new(Ui.Line, 1);
    private static readonly Color ColL = Color.FromRgb(0x4D, 0xA3, 0xFF);
    private static readonly Color ColR = Color.FromRgb(0xE3, 0x00, 0x1B);
    private const double FMin = 10, FMax = 30000;

    private List<Series> _s = new();
    private bool _auto;

    public string Caption { get; set; } = "";
    public string YTitle { get; set; } = "Level [dB]";

    public void SetSeries(IEnumerable<Series> s, bool auto) { _s = s.ToList(); _auto = auto; InvalidateVisual(); }
    public void Refresh() => InvalidateVisual();

    private static FormattedText Txt(string s, double size, IBrush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 80 || h < 80) return;
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
        double left = 62, right = 20, top = Caption.Length > 0 ? 34 : 16, bottom = 44;
        double pw = w - left - right, ph = h - top - bottom;
        if (pw < 50 || ph < 50) return;

        double hi = 10, lo = -40;
        if (_auto)
        {
            double mx = double.NegativeInfinity, mn = double.PositiveInfinity;
            foreach (var s in _s)
                foreach (var arr in new[] { s.L, s.R })
                {
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length && i < s.F.Length; i++)
                        if (!double.IsNaN(arr[i]) && s.F[i] >= 20 && s.F[i] <= 20000) { mx = Math.Max(mx, arr[i]); mn = Math.Min(mn, arr[i]); }
                }
            if (!double.IsInfinity(mx))
            {
                hi = Math.Ceiling((mx + 3) / 10) * 10;
                lo = Math.Max(Math.Floor((mn - 3) / 10) * 10, hi - 100);
                if (hi - lo < 20) lo = hi - 20;
            }
        }
        double lg0 = Math.Log10(FMin), lg1 = Math.Log10(FMax);
        double X(double f) => left + pw * (Math.Log10(Math.Max(f, FMin)) - lg0) / (lg1 - lg0);
        double Y(double db) => top + (hi - Math.Min(hi, Math.Max(lo, db))) / (hi - lo) * ph;

        for (double dec = 10; dec < FMax; dec *= 10)
            for (int m = 1; m <= 9; m++)
            {
                double f = dec * m;
                if (f > FMax) break;
                double x = X(f);
                dc.DrawLine(m == 1 ? MajorPen : MinorPen, new Point(x, top), new Point(x, top + ph));
            }
        double step = (hi - lo) <= 30 ? 5 : 10;
        for (double v = Math.Ceiling(lo / step) * step; v <= hi + 1e-9; v += step)
        {
            double y = Y(v);
            dc.DrawLine(MajorPen, new Point(left, y), new Point(left + pw, y));
            var t = Txt(v.ToString("0", CultureInfo.InvariantCulture), 11, Ui.Dim);
            dc.DrawText(t, new Point(left - 8 - t.Width, y - t.Height / 2));
        }
        dc.DrawRectangle(null, FramePen, new Rect(left, top, pw, ph));

        foreach (var (f, lab) in new[] { (10.0, "10"), (20.0, "20"), (50.0, "50"), (100.0, "100"), (200.0, "200"), (500.0, "500"),
                                         (1000.0, "1k"), (2000.0, "2k"), (5000.0, "5k"), (10000.0, "10k"), (20000.0, "20k") })
        {
            var t = Txt(lab, 11, Ui.Dim);
            dc.DrawText(t, new Point(X(f) - t.Width / 2, top + ph + 6));
        }
        var ax = Txt("Frequency [Hz]", 11, Ui.Dim);
        dc.DrawText(ax, new Point(left + pw / 2 - ax.Width / 2, h - ax.Height - 2));
        var yt = Txt(YTitle, 11, Ui.Dim);
        using (dc.PushTransform(Matrix.CreateTranslation(-12, -(top + ph / 2)) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(12, top + ph / 2)))
            dc.DrawText(yt, new Point(12 - yt.Width / 2, top + ph / 2 - yt.Height / 2));

        bool anyR = false;
        foreach (var s in _s)
        {
            Curve(dc, s.F, s.L, s.CL ?? ColL, X, Y);
            if (s.R != null) { Curve(dc, s.FR ?? s.F, s.R, s.CR ?? ColR, X, Y); anyR = true; }
            if (s.Label.Length > 0 && s.F.Length > 0)
            {
                int gi = 0; double bestD = double.MaxValue;
                for (int i = 0; i < s.F.Length; i++) { double d = Math.Abs(Math.Log(s.F[i] / 300)); if (d < bestD) { bestD = d; gi = i; } }
                double a = s.L != null && gi < s.L.Length ? s.L[gi] : double.NaN;
                double b = s.R != null && gi < s.R.Length ? s.R[gi] : double.NaN;
                double v = double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
                if (!double.IsNaN(v))
                {
                    var t = Txt(s.Label, 13, Ui.Gold);
                    dc.DrawText(t, new Point(X(300) - t.Width / 2, Y(v) - t.Height - 4));
                }
            }
        }

        var lg = Txt("Lch", 12, new SolidColorBrush(ColL));
        var sl = Txt(" / ", 12, Ui.Dim);
        var rg = Txt("Rch", 12, new SolidColorBrush(ColR));
        double lx = left + pw - lg.Width - sl.Width - (anyR ? rg.Width : 0) - 8;
        dc.DrawText(lg, new Point(lx, top + 4));
        if (anyR) { dc.DrawText(sl, new Point(lx + lg.Width, top + 4)); dc.DrawText(rg, new Point(lx + lg.Width + sl.Width, top + 4)); }
        if (Caption.Length > 0) dc.DrawText(Txt(Caption, 14, Ui.Gold), new Point(left, 8));
    }

    private static void Curve(DrawingContext dc, double[] f, double[]? v, Color col, Func<double, double> X, Func<double, double> Y)
    {
        if (v == null) return;
        var pen = new Pen(new SolidColorBrush(col), 1.0, lineJoin: PenLineJoin.Round);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool open = false;
            for (int i = 0; i < v.Length && i < f.Length; i++)
            {
                if (double.IsNaN(v[i])) { if (open) { ctx.EndFigure(false); open = false; } continue; }
                var p = new Point(X(f[i]), Y(v[i]));
                if (!open) { ctx.BeginFigure(p, false); open = true; }
                else ctx.LineTo(p);
            }
            if (open) ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geo);
    }
}
