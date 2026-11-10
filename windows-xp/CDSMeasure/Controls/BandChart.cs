using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CDSMeasure.Core;

namespace CDSMeasure.Controls;

/// <summary>Graf frekvenčnega odziva v pasovih 1/3 oktave (kot T100): L modro, D rdeče, zasenčene dekade (100 Hz, 1 kHz, 10 kHz; indeksi pasov 8, 18, 28).</summary>
public class BandChart : FrameworkElement
{
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Mono = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush BgBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C)));
    private static readonly Brush GoldBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xA9, 0x6E)));
    private static readonly Brush DecadeBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xC8, 0xA9, 0x6E)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xA9, 0x6E))), 1));
    private static readonly Pen ZeroPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xA0, 0xC8, 0xA9, 0x6E))), 1.2));
    private static readonly Pen FramePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x22))), 1));
    private static readonly Color ColL = Color.FromRgb(0x4D, 0xA3, 0xFF);
    private static readonly Color ColR = Color.FromRgb(0xE3, 0x00, 0x1B);

    /// <summary>Ena meritev (npr. pri nivoju −20 dB): kanal L, kanal D in oznaka ob krivulji.</summary>
    public sealed class Series
    {
        public double[]? L, R;
        public string Label = "";
    }

    private List<Series> _series = new();
    private double _top = 5, _span = 20;

    public string Caption { get; set; } = "";
    public string YTitle { get; set; } = "dB";
    /// <summary>Za izvoz: bela podlaga ni potrebna, graf ostane v temi programa.</summary>
    public bool ShowRight { get; set; } = true;
    /// <summary>Pas, ki ga sweep trenutno prečka (−1 = noben), in trenutna nivoja L/D (NaN = ni). Aktivni pas se nariše kot rastoč stolpec, črtica ostane na zadržanem maksimumu.</summary>
    public int LiveBand
    {
        get => _liveBand;
        set
        {
            if (value == _liveBand) return;
            _liveBand = value;
            _animL = _animR = _top - _span;                   // nov pas: stolpec zraste od spodaj
            if (value >= 0) _anim.Start(); else _anim.Stop();
        }
    }
    private int _liveBand = -1;
    private double _animL = double.NaN, _animR = double.NaN;  // prikazana (animirana) višina stolpca v dB
    private readonly System.Windows.Threading.DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public BandChart()
    {
        _anim.Tick += (_, _) =>
        {
            double bot = _top - _span;
            _animL = Approach(_animL, LiveL, bot);
            _animR = Approach(_animR, LiveR, bot);
            InvalidateVisual();
        };
        Unloaded += (_, _) => _anim.Stop();
    }

    // stolpec se približuje cilju: navzgor počasi in enakomerno (viden "dvig"), navzdol hitreje
    private double Approach(double cur, double target, double bot)
    {
        if (double.IsNaN(target)) return cur;
        if (double.IsNaN(cur)) cur = bot;
        double d = target - cur;
        if (Math.Abs(d) < 0.05) return target;
        return cur + d * 0.10;                                  // mehko približevanje (ni utripanja)
    }
    public double LiveL { get; set; } = double.NaN;
    public double LiveR { get; set; } = double.NaN;
    /// <summary>Najvišji nivo čez celoten spekter za vsak kanal (zgoraj desno ob Lch / Rch).</summary>
    public double MaxL { get; set; } = double.NaN;
    public double MaxR { get; set; } = double.NaN;

    public void SetData(double[]? l, double[]? r, double top, double span)
    {
        _series = new List<Series> { new Series { L = l, R = r } };
        _top = top; _span = span;
        InvalidateVisual();
    }

    public void SetSeries(IEnumerable<Series> series, double top, double span)
    {
        _series = series.ToList();
        _top = top; _span = span;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    private FormattedText Txt(string s, double size, Brush b) =>
        FT.New(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b, Dpi.Get(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 80) return;
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        double left = 62, right = 16, top = Caption.Length > 0 ? 34 : 16, bottom = 50;
        if (_series.Any(s => s.Label.Length > 0)) right = 64;
        double pw = w - left - right, ph = h - top - bottom;
        if (pw < 50 || ph < 50) return;
        int n = PbCore.Centers.Length;
        double slot = pw / n;
        double bot = _top - _span;
        double Y(double db) => top + (_top - Math.Min(_top, Math.Max(bot, db))) / _span * ph;

        // zasenčene dekade (100 Hz, 1 kHz, 10 kHz; indeksi pasov 8, 18, 28)
        foreach (int bi in new[] { 8, 18, 28 })
            dc.DrawRectangle(DecadeBrush, null, new Rect(left + bi * slot, top, slot, ph));

        // vodoravne črte in oznake dB
        double step = _span <= 12 ? 2 : _span <= 60 ? 5 : 10;
        for (double v = Math.Ceiling(bot / step) * step; v <= _top + 1e-9; v += step)
        {
            double y = Y(v);
            dc.DrawLine(Math.Abs(v) < 1e-9 ? ZeroPen : GridPen, new Point(left, y), new Point(left + pw, y));
            var t = Txt(v.ToString("0", CultureInfo.InvariantCulture), 11, DimBrush);
            dc.DrawText(t, new Point(left - 8 - t.Width, y - t.Height / 2));
        }
        dc.DrawRectangle(null, FramePen, new Rect(left, top, pw, ph));

        // oznake pasov
        bool every = slot >= 30;
        for (int i = 0; i < n; i++)
        {
            if (!every && i % 2 == 1) continue;
            var t = Txt(PbCore.Labels[i], 11, DimBrush);
            dc.DrawText(t, new Point(left + (i + 0.5) * slot - t.Width / 2, top + ph + 6 + (every && i % 2 == 1 ? 12 : 0)));
        }
        var ax = Txt("Frequency [Hz]", 11, DimBrush);
        dc.DrawText(ax, new Point(left + pw / 2 - ax.Width / 2, h - ax.Height - 1));
        var yt = Txt(YTitle, 11, DimBrush);
        dc.PushTransform(new RotateTransform(-90, 12, top + ph / 2));
        dc.DrawText(yt, new Point(12 - yt.Width / 2, top + ph / 2 - yt.Height / 2));
        dc.Pop();

        // podatki: kratke vodoravne črte po pasovih (L levo, D desno od središča) + tanka povezovalna črta
        foreach (var s in _series)
        {
            var pl = DrawSeries(dc, s.L, ColL, -1, left, slot, Y, _animL, bot);
            Point? pr = ShowRight ? DrawSeries(dc, s.R, ColR, +1, left, slot, Y, _animR, bot) : null;
            if (s.Label.Length > 0)
            {
                // oznaka nivoja ob koncu krivulje (zlato)
                Point? p = pl != null && pr != null ? (pl.Value.X >= pr.Value.X ? pl : pr) : (pl ?? pr);
                if (p != null)
                {
                    var t = Txt(s.Label, 13, GoldBrush);
                    dc.DrawText(t, new Point(Math.Min(p.Value.X + slot * 0.45, w - t.Width - 2), p.Value.Y - t.Height / 2));
                }
            }
        }

        // legenda in napis
        string NumTxt(double v) => double.IsNaN(v) ? "" : " " + v.ToString("0.0", CultureInfo.InvariantCulture);
        var lg = Txt("Lch", 12, Frozen(new SolidColorBrush(ColL)));
        var ln = Txt(NumTxt(MaxL), 12, Frozen(new SolidColorBrush(ColL)));
        var sl = Txt("  /  ", 12, DimBrush);
        var rg = Txt("Rch", 12, Frozen(new SolidColorBrush(ColR)));
        var rn = Txt(NumTxt(MaxR), 12, Frozen(new SolidColorBrush(ColR)));
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
        if (Caption.Length > 0)
            dc.DrawText(Txt(Caption, 14, GoldBrush), new Point(left, 8));
    }

    private Point? DrawSeries(DrawingContext dc, double[]? v, Color col, int side, double left, double slot, Func<double, double> Y, double live, double bot)
    {
        if (v == null) return null;
        var brush = Frozen(new SolidColorBrush(col));
        var fill = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, col.R, col.G, col.B)));
        var bar = Frozen(new Pen(brush, 1.8) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
        var thin = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x90, col.R, col.G, col.B))), 0.7));
        Point? prev = null, last = null;
        for (int i = 0; i < v.Length; i++)
        {
            if (double.IsNaN(v[i])) { prev = null; continue; }
            double cx = left + (i + 0.5) * slot + side * slot * 0.22;
            double y = Y(v[i]);
            if (i == LiveBand && !double.IsNaN(live))
            {
                // trenutni nivo aktivnega pasu: poln stolpec od spodaj navzgor (črtica spodaj ostane na maksimumu)
                double yl = Y(live), yb = Y(bot);
                if (yb - yl >= 1) dc.DrawRectangle(fill, null, new Rect(cx - slot * 0.20, yl, slot * 0.40, yb - yl));
            }
            dc.DrawLine(bar, new Point(cx - slot * 0.20, y), new Point(cx + slot * 0.20, y));
            var p = new Point(cx, y);
            if (prev != null) dc.DrawLine(thin, prev.Value, p);
            prev = p; last = new Point(cx + slot * 0.20, y);
        }
        return last;
    }
}
