using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CDSMeasure.Core;

namespace CDSMeasure.Controls;

/// <summary>Graf frekvenčnega odziva v pasovih 1/3 oktave: L modro, D rdeče, zasenčene dekade.</summary>
public sealed class BandChart : Control
{
    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    private static readonly IBrush BgBrush = Brush.Parse("#0A0A0A");
    private static readonly IBrush DecadeBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xC8, 0xA9, 0x6E));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xA9, 0x6E)), 1);
    private static readonly Pen ZeroPen = new(new SolidColorBrush(Color.FromArgb(0xA0, 0xC8, 0xA9, 0x6E)), 1.2);
    private static readonly Pen FramePen = new(Ui.Line, 1);
    private static readonly Color ColL = Color.FromRgb(0x4D, 0xA3, 0xFF);
    private static readonly Color ColR = Color.FromRgb(0xE3, 0x00, 0x1B);

    public sealed class Series
    {
        public double[]? L, R;
        public string Label = "";
    }

    private List<Series> _series = new();
    private double _top = 5, _span = 20;
    private int _liveBand = -1;
    private double _animL = double.NaN, _animR = double.NaN;
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public string Caption { get; set; } = "";
    public string YTitle { get; set; } = "dB";
    public bool ShowRight { get; set; } = true;
    public double LiveL { get; set; } = double.NaN;
    public double LiveR { get; set; } = double.NaN;
    public double MaxL { get; set; } = double.NaN;
    public double MaxR { get; set; } = double.NaN;

    public int LiveBand
    {
        get => _liveBand;
        set
        {
            if (value == _liveBand) return;
            _liveBand = value;
            _animL = _animR = _top - _span;
            if (value >= 0) _anim.Start(); else _anim.Stop();
        }
    }

    public BandChart()
    {
        _anim.Tick += (_, _) =>
        {
            double bot = _top - _span;
            _animL = Approach(_animL, LiveL, bot);
            _animR = Approach(_animR, LiveR, bot);
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _anim.Stop();
    }

    private static double Approach(double cur, double target, double bot)
    {
        if (double.IsNaN(target)) return cur;
        if (double.IsNaN(cur)) cur = bot;
        double d = target - cur;
        return Math.Abs(d) < 0.05 ? target : cur + d * 0.15;
    }

    public void SetSeries(IEnumerable<Series> series, double top, double span)
    {
        _series = series.ToList(); _top = top; _span = span;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    private static FormattedText Txt(string s, double size, IBrush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 80 || h < 80) return;
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        double left = 62, right = 16, top = Caption.Length > 0 ? 34 : 16, bottom = 50;
        double pw = w - left - right, ph = h - top - bottom;
        if (pw < 50 || ph < 50) return;
        int n = PbCore.Centers.Length;
        double slot = pw / n;
        double bot = _top - _span;
        double Y(double db) => top + (_top - Math.Min(_top, Math.Max(bot, db))) / _span * ph;

        foreach (int bi in new[] { 8, 18, 28 })
            dc.DrawRectangle(DecadeBrush, null, new Rect(left + bi * slot, top, slot, ph));

        double step = _span <= 12 ? 2 : _span <= 60 ? 5 : 10;
        for (double v = Math.Ceiling(bot / step) * step; v <= _top + 1e-9; v += step)
        {
            double y = Y(v);
            dc.DrawLine(Math.Abs(v) < 1e-9 ? ZeroPen : GridPen, new Point(left, y), new Point(left + pw, y));
            var t = Txt(v.ToString("0", CultureInfo.InvariantCulture), 11, Ui.Dim);
            dc.DrawText(t, new Point(left - 8 - t.Width, y - t.Height / 2));
        }
        dc.DrawRectangle(null, FramePen, new Rect(left, top, pw, ph));

        bool every = slot >= 30;
        for (int i = 0; i < n; i++)
        {
            if (!every && i % 2 == 1) continue;
            var t = Txt(PbCore.Labels[i], 11, Ui.Dim);
            dc.DrawText(t, new Point(left + (i + 0.5) * slot - t.Width / 2, top + ph + 6));
        }
        var ax = Txt("Frequency [Hz]", 11, Ui.Dim);
        dc.DrawText(ax, new Point(left + pw / 2 - ax.Width / 2, h - ax.Height - 1));
        var yt = Txt(YTitle, 11, Ui.Dim);
        using (dc.PushTransform(Matrix.CreateTranslation(-12, -(top + ph / 2)) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(12, top + ph / 2)))
            dc.DrawText(yt, new Point(12 - yt.Width / 2, top + ph / 2 - yt.Height / 2));

        foreach (var s in _series)
        {
            DrawSeries(dc, s.L, ColL, -1, left, slot, Y, _animL, bot);
            if (ShowRight) DrawSeries(dc, s.R, ColR, +1, left, slot, Y, _animR, bot);
        }

        string NumTxt(double v) => double.IsNaN(v) ? "" : " " + v.ToString("0.0", CultureInfo.InvariantCulture);
        var bl = new SolidColorBrush(ColL); var br = new SolidColorBrush(ColR);
        var lg = Txt("Lch", 12, bl); var ln = Txt(NumTxt(MaxL), 12, bl);
        var sl = Txt("  /  ", 12, Ui.Dim);
        var rg = Txt("Rch", 12, br); var rn = Txt(NumTxt(MaxR), 12, br);
        double tot = lg.Width + ln.Width + (ShowRight ? sl.Width + rg.Width + rn.Width : 0);
        double lx = left + pw - tot - 8;
        dc.DrawText(lg, new Point(lx, top + 4)); lx += lg.Width;
        dc.DrawText(ln, new Point(lx, top + 4)); lx += ln.Width;
        if (ShowRight)
        {
            dc.DrawText(sl, new Point(lx, top + 4)); lx += sl.Width;
            dc.DrawText(rg, new Point(lx, top + 4)); lx += rg.Width;
            dc.DrawText(rn, new Point(lx, top + 4));
        }
        if (Caption.Length > 0) dc.DrawText(Txt(Caption, 14, Ui.Gold), new Point(left, 8));
    }

    private void DrawSeries(DrawingContext dc, double[]? v, Color col, int side, double left, double slot, Func<double, double> Y, double live, double bot)
    {
        if (v == null) return;
        var brush = new SolidColorBrush(col);
        var fill = new SolidColorBrush(Color.FromArgb(0xC0, col.R, col.G, col.B));
        var bar = new Pen(brush, 1.8, lineCap: PenLineCap.Flat);
        var thin = new Pen(new SolidColorBrush(Color.FromArgb(0x90, col.R, col.G, col.B)), 0.7);
        Point? prev = null;
        for (int i = 0; i < v.Length; i++)
        {
            if (double.IsNaN(v[i])) { prev = null; continue; }
            double cx = left + (i + 0.5) * slot + side * slot * 0.22;
            double y = Y(v[i]);
            if (i == LiveBand && !double.IsNaN(live))
            {
                double yl = Y(live), yb = Y(bot);
                if (yb - yl >= 1) dc.DrawRectangle(fill, null, new Rect(cx - slot * 0.20, yl, slot * 0.40, yb - yl));
            }
            dc.DrawLine(bar, new Point(cx - slot * 0.20, y), new Point(cx + slot * 0.20, y));
            var p = new Point(cx, y);
            if (prev != null) dc.DrawLine(thin, prev.Value, p);
            prev = p;
        }
    }
}
