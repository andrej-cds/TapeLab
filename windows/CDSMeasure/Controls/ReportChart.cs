using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CDSMeasure.Controls;

public class ChartSeries
{
    public string Key { get; set; } = "";      // wrms | din | hz | t_wrms | t_din | t_hz
    public string Name { get; set; } = "";
    public IReadOnlyList<Point> Points { get; set; } = Array.Empty<Point>();
    public bool Secondary { get; set; }        // desna os (Hz)
    public bool Trend { get; set; }
}

/// <summary>
/// Skupni graf W&amp;F&amp;S (kot Andrejev Excelov graf): W&amp;F % levo, hitrost Hz desno, tri serije + linearni trendi.
/// Print = bela podlaga za poročilo stranki.
/// </summary>
public class ReportChart : FrameworkElement
{
    private static readonly Typeface Mono = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MonoBold = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public List<ChartSeries> Series { get; } = new();
    public bool Print { get; set; }
    public bool FixedAxes { get; set; } = true;
    public double CenterHz { get; set; } = 3000;          // sredina desne osi: 3000 ali 3150 Hz (±50 Hz)
    public double XMax { get; set; } = 2700;              // sekunde
    public string Caption { get; set; } = "";
    public string LeftTitle { get; set; } = "W&F [%]";
    public string RightTitle { get; set; } = "Speed [Hz]";
    public string XTitle { get; set; } = "Time [min]";

    public void Refresh() => InvalidateVisual();

    public static Color SeriesColor(string key, bool print) => key switch
    {
        "wrms" => print ? Color.FromRgb(0x1E, 0x8E, 0x5A) : Color.FromRgb(0x4F, 0xD0, 0x8F),
        "din" => print ? Color.FromRgb(0xE0, 0x7B, 0x0B) : Color.FromRgb(0xF5, 0xA6, 0x3C),
        "hz" => print ? Color.FromRgb(0x1F, 0x2F, 0xC8) : Color.FromRgb(0x78, 0x8C, 0xFF),
        "t_wrms" => print ? Color.FromRgb(0xD0, 0x20, 0x20) : Color.FromRgb(0xFF, 0x5A, 0x5A),
        "t_din" => print ? Color.FromRgb(0x00, 0x00, 0x00) : Color.FromRgb(0xF0, 0xED, 0xE8),
        _ => print ? Color.FromRgb(0x77, 0x77, 0x77) : Color.FromRgb(0xA8, 0xA3, 0x9A),
    };

    private static double NiceCeil(double v)
    {
        if (v <= 0) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (v <= m * mag + 1e-12) return m * mag;
        return 10 * mag;
    }

    private static Geometry Build(IReadOnlyList<Point> pts, Func<double, double> X, Func<double, double> Y, Rect plot)
    {
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            int n = pts.Count;
            if (n < 2) { sg.Freeze(); return sg; }
            // gostota kot pri klasičnem Excelovem grafu: največ ena točka na sekundo (povprečje),
            // tako da se odstopanje lepo vidi in črta ni zamazana
            double dur = pts[n - 1].X - pts[0].X;
            IReadOnlyList<Point> use = pts;
            if (dur > 0 && n > dur * 1.2)
            {
                var bins = new List<Point>();
                double sum = 0, tsum = 0; int cnt = 0; long cur = long.MinValue;
                void Flush() { if (cnt > 0) bins.Add(new Point(tsum / cnt, sum / cnt)); sum = 0; tsum = 0; cnt = 0; }
                for (int i = 0; i < n; i++)
                {
                    var q = pts[i];
                    if (double.IsNaN(q.Y)) continue;
                    long k = (long)Math.Floor(q.X);
                    if (k != cur) { Flush(); cur = k; }
                    sum += q.Y; tsum += q.X; cnt++;
                }
                Flush();
                use = bins; n = bins.Count;
                if (n < 2) { sg.Freeze(); return sg; }
            }
            bool pen = false;
            for (int i = 0; i < n; i++)
            {
                var p = use[i];
                if (double.IsNaN(p.Y)) { pen = false; continue; }
                var q = new Point(X(p.X), Y(p.Y));
                if (!pen) { c.BeginFigure(q, false, false); pen = true; }
                else c.LineTo(q, true, false);
            }
        }
        sg.Freeze();
        return sg;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 240 || h < 160) return;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        bool p = Print;

        Brush bg = p ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A));
        Brush fg = p ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0xF0, 0xED, 0xE8));
        Brush dim = p ? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)) : new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C));
        var gridPen = new Pen(p ? new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)) : new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xA9, 0x6E)), p ? 0.8 : 1);
        var framePen = new Pen(p ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x22)), p ? 1.4 : 1);

        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        double L = 74, R = w - 80, T = 42, B = h - (Caption.Length > 0 ? 66 : 48);
        var plot = new Rect(L, T, R - L, B - T);
        if (plot.Width < 50 || plot.Height < 50) return;
        int N = FixedAxes ? 20 : 10;

        // obsegi osi
        double lMax, rLo, rHi;
        if (FixedAxes) { lMax = 0.20; rLo = CenterHz - 50; rHi = CenterHz + 50; }
        else
        {
            double pMax = 0, sMin = double.MaxValue, sMax = double.MinValue;
            foreach (var s in Series)
                foreach (var pt in s.Points)
                {
                    if (double.IsNaN(pt.Y)) continue;
                    if (s.Secondary) { sMin = Math.Min(sMin, pt.Y); sMax = Math.Max(sMax, pt.Y); }
                    else pMax = Math.Max(pMax, pt.Y);
                }
            lMax = NiceCeil(Math.Max(pMax * 1.05, 0.01));
            if (sMax < sMin) { sMin = 2990; sMax = 3010; }
            double span = Math.Max(10, (sMax - sMin) * 1.1);
            double step = NiceCeil(span / N);
            rLo = Math.Floor(sMin / step) * step;
            rHi = rLo + N * step;
            while (rHi < sMax) { rLo += step; rHi += step; }
        }

        FormattedText Ft(string s, Brush b, double size, bool bold = false) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? MonoBold : Mono, size, b, ppd);

        // vodoravna mreža in oznake obeh osi (deljenja se ujemajo)
        string lfmt = lMax <= 1 ? "0.000" : "0.00";
        double rStep = (rHi - rLo) / N;
        string rfmt = rStep >= 1 ? "0" : "0.0";
        for (int i = 0; i <= N; i++)
        {
            double y = B - i * plot.Height / N;
            dc.DrawLine(gridPen, new Point(L, y), new Point(R, y));
            var fl = Ft((i * lMax / N).ToString(lfmt, CultureInfo.CurrentCulture), dim, 11);
            dc.DrawText(fl, new Point(L - 8 - fl.Width, y - fl.Height / 2));
            var fr = Ft((rLo + i * rStep).ToString(rfmt, CultureInfo.CurrentCulture), dim, 11);
            dc.DrawText(fr, new Point(R + 8, y - fr.Height / 2));
        }

        // X os v sekundah (kot v Excelovem grafu); navpična mreža samo na zaslonu, v tiskani izvedbi je ni
        double xmax = Math.Max(XMax, 1);
        double stepS = 1;
        foreach (double c in new[] { 1.0, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 }) { stepS = c; if (xmax / c <= 10) break; }
        for (double sec = 0; sec <= xmax + 1e-9; sec += stepS)
        {
            double x = L + sec / xmax * plot.Width;
            if (!p) dc.DrawLine(gridPen, new Point(x, T), new Point(x, B));
            var ft = Ft(sec.ToString("0", CultureInfo.CurrentCulture), dim, 11);
            dc.DrawText(ft, new Point(x - ft.Width / 2, B + 8));
        }

        // serije
        dc.PushClip(new RectangleGeometry(plot));
        foreach (var s in Series)
        {
            // debelina črte sorazmerna s širino grafa (v programu ~1700 px = 1,0); hitrost (Hz) je 2/3 tanjša od WRMS/DIN
            double kw = Math.Clamp(plot.Width / 1700.0, 0.45, 1.0);
            double th = s.Trend ? 1.8 * Math.Max(kw, 0.7) : (s.Key == "hz" ? kw * 2.0 / 3.0 : kw);
            var pen = new Pen(new SolidColorBrush(SeriesColor(s.Key, p)), th);
            if (s.Trend && (s.Key == "t_wrms" || s.Key == "t_hz")) pen.DashStyle = DashStyles.Dash;
            Func<double, double> Y = s.Secondary
                ? (v => B - (v - rLo) / (rHi - rLo) * plot.Height)
                : (v => B - v / lMax * plot.Height);
            dc.DrawGeometry(null, pen, Build(s.Points, x => L + x / xmax * plot.Width, Y, plot));
        }
        dc.Pop();
        dc.DrawRectangle(null, framePen, plot);
        if (p) dc.DrawLine(new Pen(Brushes.Black, 4), new Point(L, B), new Point(R, B));

        // legenda (kot v Excelu: okvir čez celotno širino, vzorec črte + ime)
        if (p) dc.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(L, 6, plot.Width, 26));
        double slot = plot.Width / Math.Max(1, Series.Count);
        double lx = L;
        for (int i = 0; i < Series.Count; i++)
        {
            var s1 = Series[i];
            var pen = new Pen(new SolidColorBrush(SeriesColor(s1.Key, p)), s1.Trend ? 1.8 : 1.6);
            if (s1.Trend && (s1.Key == "t_wrms" || s1.Key == "t_hz")) pen.DashStyle = DashStyles.Dash;
            var ft = Ft(s1.Name, fg, 12);
            double x0 = p ? L + i * slot + 10 : lx;
            if (!p && x0 + 40 + ft.Width > R + 60) break;
            dc.DrawLine(pen, new Point(x0, 19), new Point(x0 + 28, 19));
            dc.DrawText(ft, new Point(x0 + 34, 19 - ft.Height / 2));
            lx = x0 + 34 + ft.Width + 22;
        }

        // naslovi osi
        void Rot(string s, double cx, double cy, double angle)
        {
            var f = Ft(s, fg, 13, true);
            dc.PushTransform(new RotateTransform(angle, cx, cy));
            dc.DrawText(f, new Point(cx - f.Width / 2, cy - f.Height / 2));
            dc.Pop();
        }
        Rot(LeftTitle, 18, (T + B) / 2, -90);
        Rot(RightTitle, w - 18, (T + B) / 2, 90);
        var xt = Ft(XTitle, fg, 13, true);
        dc.DrawText(xt, new Point((L + R) / 2 - xt.Width / 2, B + 24));
        if (Caption.Length > 0)
            dc.DrawText(Ft(Caption, fg, 15), new Point(L, h - 30));
    }
}
