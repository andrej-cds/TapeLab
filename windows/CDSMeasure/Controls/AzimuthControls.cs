using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CDSMeasure.Controls;

/// <summary>Skupna orodja za risanje besedila.</summary>
internal static class AzDraw
{
    public static Brush B(string key) => (Brush)Application.Current.Resources[key];

    public static FormattedText Text(Visual v, string s, double size, Brush brush, string font = "FontMono")
    {
        var ff = (FontFamily)Application.Current.Resources[font];
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(v).PixelsPerDip);
    }
}

/// <summary>Vodoravna skala s kazalcem: fazna razlika L − D v stopinjah, zeleno ciljno območje okoli ničle.</summary>
public sealed class AzimuthGauge : FrameworkElement
{
    private double _phase, _range = 90, _zone = 10;
    private bool _valid;

    public void Set(double phaseDeg, bool valid, double rangeDeg, double zoneDeg)
    {
        _phase = phaseDeg; _valid = valid; _range = Math.Max(1, rangeDeg); _zone = Math.Max(0, zoneDeg);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 40) return;
        double left = 36, right = w - 36, cx = (left + right) / 2, half = (right - left) / 2;
        double top = h * 0.28, bot = h * 0.62;
        var line = new Pen(AzDraw.B("LineBrush"), 1);
        var gold = AzDraw.B("GoldBrush");

        dc.DrawRectangle(AzDraw.B("PanelBrush"), line, new Rect(left, top, right - left, bot - top));
        double zw = Math.Min(half, _zone / _range * half);
        var zoneBrush = new SolidColorBrush(Color.FromArgb(60, 70, 200, 110));
        dc.DrawRectangle(zoneBrush, null, new Rect(cx - zw, top, zw * 2, bot - top));

        // oznake skale
        double step = _range / 3;
        for (int i = -3; i <= 3; i++)
        {
            double x = cx + i * step / _range * half;
            dc.DrawLine(new Pen(gold, i == 0 ? 2 : 1), new Point(x, top), new Point(x, bot));
            var t = AzDraw.Text(this, (i * step).ToString("+0;-0;0", CultureInfo.InvariantCulture) + "°", 12, AzDraw.B("DimBrush"));
            dc.DrawText(t, new Point(x - t.Width / 2, bot + 6));
        }

        if (!_valid)
        {
            var t = AzDraw.Text(this, "—", 28, AzDraw.B("DimBrush"));
            dc.DrawText(t, new Point(cx - t.Width / 2, top + (bot - top - t.Height) / 2));
            return;
        }
        double p = Math.Max(-_range, Math.Min(_range, _phase));
        bool clamped = Math.Abs(_phase) > _range;
        bool inZone = Math.Abs(_phase) <= _zone;
        double nx = cx + p / _range * half;
        Brush nb = clamped ? AzDraw.B("RedBrush") : inZone ? new SolidColorBrush(Color.FromRgb(90, 220, 130)) : AzDraw.B("GoldLightBrush");
        dc.DrawRectangle(nb, null, new Rect(nx - 3, top - 12, 6, bot - top + 24));
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(nx - 9, top - 12), true, true);
            g.LineTo(new Point(nx + 9, top - 12), true, false);
            g.LineTo(new Point(nx, top + 2), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(nb, null, tri);
    }
}

/// <summary>Potek fazne razlike v zadnjih sekundah (za spremljanje, medtem ko vrtiš vijak).</summary>
public sealed class PhaseTrace : FrameworkElement
{
    private readonly List<(double T, double Deg)> _pts = new();
    private double _range = 90, _zone = 10, _window = 30;
    private double _now;

    public void Clear() { _pts.Clear(); InvalidateVisual(); }

    public void Add(double t, double deg)
    {
        _now = t; _pts.Add((t, deg));
        int cut = 0;
        while (cut < _pts.Count && _pts[cut].T < t - _window) cut++;
        if (cut > 0) _pts.RemoveRange(0, cut);
    }

    public void Set(double rangeDeg, double zoneDeg, double nowT)
    {
        _range = Math.Max(1, rangeDeg); _zone = Math.Max(0, zoneDeg); _now = nowT;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 30) return;
        double left = 36, right = w - 12, top = 8, bot = h - 8;
        double cy = (top + bot) / 2, half = (bot - top) / 2;
        dc.DrawRectangle(AzDraw.B("PanelBrush"), new Pen(AzDraw.B("LineBrush"), 1), new Rect(left, top, right - left, bot - top));
        double zh = Math.Min(half, _zone / _range * half);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 70, 200, 110)), null, new Rect(left, cy - zh, right - left, zh * 2));
        dc.DrawLine(new Pen(AzDraw.B("GoldBrush"), 1), new Point(left, cy), new Point(right, cy));
        var tp = AzDraw.Text(this, "0°", 11, AzDraw.B("DimBrush")); dc.DrawText(tp, new Point(left - tp.Width - 4, cy - tp.Height / 2));
        var tt = AzDraw.Text(this, "+" + _range.ToString("0", CultureInfo.InvariantCulture) + "°", 11, AzDraw.B("DimBrush")); dc.DrawText(tt, new Point(left - tt.Width - 4, top));
        var tb = AzDraw.Text(this, "−" + _range.ToString("0", CultureInfo.InvariantCulture) + "°", 11, AzDraw.B("DimBrush")); dc.DrawText(tb, new Point(left - tb.Width - 4, bot - tb.Height));
        if (_pts.Count < 2) return;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            bool first = true;
            foreach (var (t, deg) in _pts)
            {
                double x = right - (_now - t) / _window * (right - left);
                double y = cy - Math.Max(-_range, Math.Min(_range, deg)) / _range * half;
                if (first) { g.BeginFigure(new Point(x, y), false, false); first = false; }
                else g.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(AzDraw.B("GoldLightBrush"), 2) { LineJoin = PenLineJoin.Round }, geo);
    }
}

/// <summary>Lissajousov prikaz: L na osi X, R na osi Y. Pri enakem signalu v fazi je lik črta pod 45° (od spodaj levo navzgor desno),
/// pri razliki faze se odpre v elipso. Zelena elipsa kaže izbrano tolerančno območje azimuta.</summary>
public sealed class LissajousScope : FrameworkElement
{
    private float[] _l = Array.Empty<float>(), _r = Array.Empty<float>();
    private int _n;
    private double _zoneDeg = 10;

    public void Set(float[] l, float[] r, int n, double zoneDeg)
    {
        _l = l; _r = r; _n = n; _zoneDeg = Math.Max(0, zoneDeg);
        InvalidateVisual();
    }

    public void Clear() { _n = 0; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double side = Math.Min(w, h) - 4;
        if (side < 60) return;
        double cx = w / 2, cy = h / 2, half = side / 2;
        var rect = new Rect(cx - half, cy - half, side, side);
        var dim = AzDraw.B("DimBrush");
        dc.DrawRectangle(AzDraw.B("PanelBrush"), new Pen(AzDraw.B("LineBrush"), 1), rect);

        // osi in krožnica
        var axis = new Pen(AzDraw.B("LineBrush"), 1);
        dc.DrawLine(axis, new Point(cx - half, cy), new Point(cx + half, cy));
        dc.DrawLine(axis, new Point(cx, cy - half), new Point(cx, cy + half));
        dc.DrawEllipse(null, axis, new Point(cx, cy), half * 0.8, half * 0.8);

        // diagonala L = R (ideal)
        var diag = new Pen(AzDraw.B("GoldBrush"), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(diag, new Point(cx - half, cy + half), new Point(cx + half, cy - half));

        // zelena elipsa za tolerančno območje (pri enakih amplitudah): polosi √2·A·cos(φ/2) in √2·A·sin(φ/2) vzdolž diagonal
        double A = half * 0.8, phi = _zoneDeg * Math.PI / 180;
        double rx = Math.Sqrt(2) * A * Math.Cos(phi / 2), ry = Math.Max(1.0, Math.Sqrt(2) * A * Math.Sin(phi / 2));
        dc.PushTransform(new RotateTransform(-45, cx, cy));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(36, 70, 200, 110)), new Pen(new SolidColorBrush(Color.FromArgb(150, 70, 200, 110)), 1.2), new Point(cx, cy), rx, ry);
        dc.Pop();

        // oznake osi
        var tl = AzDraw.Text(this, "L →", 11, dim); dc.DrawText(tl, new Point(cx + half - tl.Width - 4, cy + 3));
        var tr = AzDraw.Text(this, "R ↑", 11, dim); dc.DrawText(tr, new Point(cx + 4, cy - half + 2));

        if (_n < 8) return;
        double max = 0;
        for (int i = 0; i < _n; i++) { double a = Math.Abs(_l[i]), b = Math.Abs(_r[i]); if (a > max) max = a; if (b > max) max = b; }
        if (max < 1e-4) return;
        double k = A / max;         // skupno merilo za oba kanala: razlika amplitud se vidi v obliki

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(cx + _l[0] * k, cy - _r[0] * k), false, false);
            for (int i = 1; i < _n; i++) g.LineTo(new Point(cx + _l[i] * k, cy - _r[i] * k), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(AzDraw.B("GoldLightBrush"), 1.4) { LineJoin = PenLineJoin.Round }, geo);
    }
}
