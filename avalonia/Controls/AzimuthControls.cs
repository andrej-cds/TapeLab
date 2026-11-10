using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CDSMeasure.Controls;

internal static class AzDraw
{
    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    public static FormattedText Text(string s, double size, IBrush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);
    public static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(90, 220, 130));
    public static readonly IBrush ZoneFill = new SolidColorBrush(Color.FromArgb(60, 70, 200, 110));
}

/// <summary>Vodoravna skala s kazalcem: fazna razlika L − D v stopinjah, zeleno ciljno območje okoli ničle.</summary>
public sealed class AzimuthGauge : Control
{
    private double _phase, _range = 90, _zone = 10;
    private bool _valid;

    public void Set(double phaseDeg, bool valid, double rangeDeg, double zoneDeg)
    {
        _phase = phaseDeg; _valid = valid; _range = Math.Max(1, rangeDeg); _zone = Math.Max(0, zoneDeg);
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 60 || h < 40) return;
        double left = 36, right = w - 36, cx = (left + right) / 2, half = (right - left) / 2;
        double top = h * 0.28, bot = h * 0.62;
        var line = new Pen(Ui.Line, 1);

        dc.DrawRectangle(Ui.Panel, line, new Rect(left, top, right - left, bot - top));
        double zw = Math.Min(half, _zone / _range * half);
        dc.DrawRectangle(AzDraw.ZoneFill, null, new Rect(cx - zw, top, zw * 2, bot - top));

        double step = _range / 3;
        for (int i = -3; i <= 3; i++)
        {
            double x = cx + i * step / _range * half;
            dc.DrawLine(new Pen(Ui.Gold, i == 0 ? 2 : 1), new Point(x, top), new Point(x, bot));
            var t = AzDraw.Text((i * step).ToString("+0;-0;0", CultureInfo.InvariantCulture) + "°", 12, Ui.Dim);
            dc.DrawText(t, new Point(x - t.Width / 2, bot + 6));
        }

        if (!_valid)
        {
            var t = AzDraw.Text("—", 28, Ui.Dim);
            dc.DrawText(t, new Point(cx - t.Width / 2, top + (bot - top - t.Height) / 2));
            return;
        }
        double p = Math.Max(-_range, Math.Min(_range, _phase));
        bool clamped = Math.Abs(_phase) > _range;
        bool inZone = Math.Abs(_phase) <= _zone;
        double nx = cx + p / _range * half;
        IBrush nb = clamped ? Ui.Red : inZone ? AzDraw.Green : Ui.GoldLight;
        dc.DrawRectangle(nb, null, new Rect(nx - 3, top - 12, 6, bot - top + 24));
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(nx - 9, top - 12), true);
            g.LineTo(new Point(nx + 9, top - 12));
            g.LineTo(new Point(nx, top + 2));
            g.EndFigure(true);
        }
        dc.DrawGeometry(nb, null, tri);
    }
}

/// <summary>Potek fazne razlike v zadnjih sekundah.</summary>
public sealed class PhaseTrace : Control
{
    private readonly List<(double T, double Deg)> _pts = new();
    private double _range = 90, _zone = 10, _window = 30, _now;

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

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 60 || h < 30) return;
        double left = 36, right = w - 12, top = 8, bot = h - 8;
        double cy = (top + bot) / 2, half = (bot - top) / 2;
        dc.DrawRectangle(Ui.Panel, new Pen(Ui.Line, 1), new Rect(left, top, right - left, bot - top));
        double zh = Math.Min(half, _zone / _range * half);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 70, 200, 110)), null, new Rect(left, cy - zh, right - left, zh * 2));
        dc.DrawLine(new Pen(Ui.Gold, 1), new Point(left, cy), new Point(right, cy));
        var inv = CultureInfo.InvariantCulture;
        var tp = AzDraw.Text("0°", 11, Ui.Dim); dc.DrawText(tp, new Point(left - tp.Width - 4, cy - tp.Height / 2));
        var tt = AzDraw.Text("+" + _range.ToString("0", inv) + "°", 11, Ui.Dim); dc.DrawText(tt, new Point(left - tt.Width - 4, top));
        var tb = AzDraw.Text("−" + _range.ToString("0", inv) + "°", 11, Ui.Dim); dc.DrawText(tb, new Point(left - tb.Width - 4, bot - tb.Height));
        if (_pts.Count < 2) return;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            bool first = true;
            foreach (var (t, deg) in _pts)
            {
                double x = right - (_now - t) / _window * (right - left);
                double y = cy - Math.Max(-_range, Math.Min(_range, deg)) / _range * half;
                if (first) { g.BeginFigure(new Point(x, y), false); first = false; }
                else g.LineTo(new Point(x, y));
            }
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(Ui.GoldLight, 2, lineJoin: PenLineJoin.Round), geo);
    }
}

/// <summary>Lissajous: L na X, R na Y. Enak signal v fazi = črta pod 45°; razlika faze odpre elipso.</summary>
public sealed class LissajousScope : Control
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

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double side = Math.Min(w, h) - 4;
        if (side < 60) return;
        double cx = w / 2, cy = h / 2, half = side / 2;
        dc.DrawRectangle(Ui.Panel, new Pen(Ui.Line, 1), new Rect(cx - half, cy - half, side, side));

        var axis = new Pen(Ui.Line, 1);
        dc.DrawLine(axis, new Point(cx - half, cy), new Point(cx + half, cy));
        dc.DrawLine(axis, new Point(cx, cy - half), new Point(cx, cy + half));
        dc.DrawEllipse(null, axis, new Point(cx, cy), half * 0.8, half * 0.8);
        dc.DrawLine(new Pen(Ui.Gold, 1, new DashStyle(new double[] { 4, 3 }, 0)), new Point(cx - half, cy + half), new Point(cx + half, cy - half));

        double A = half * 0.8, phi = _zoneDeg * Math.PI / 180;
        double rx = Math.Sqrt(2) * A * Math.Cos(phi / 2), ry = Math.Max(1.0, Math.Sqrt(2) * A * Math.Sin(phi / 2));
        using (dc.PushTransform(Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(-Math.PI / 4) * Matrix.CreateTranslation(cx, cy)))
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(36, 70, 200, 110)), new Pen(new SolidColorBrush(Color.FromArgb(150, 70, 200, 110)), 1.2), new Point(cx, cy), rx, ry);

        var tl = AzDraw.Text("L →", 11, Ui.Dim); dc.DrawText(tl, new Point(cx + half - tl.Width - 4, cy + 3));
        var tr = AzDraw.Text("R ↑", 11, Ui.Dim); dc.DrawText(tr, new Point(cx + 4, cy - half + 2));

        if (_n < 8) return;
        double max = 0;
        for (int i = 0; i < _n; i++) { double a = Math.Abs(_l[i]), b = Math.Abs(_r[i]); if (a > max) max = a; if (b > max) max = b; }
        if (max < 1e-4) return;
        double k = A / max;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(cx + _l[0] * k, cy - _r[0] * k), false);
            for (int i = 1; i < _n; i++) g.LineTo(new Point(cx + _l[i] * k, cy - _r[i] * k));
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(Ui.GoldLight, 1.4, lineJoin: PenLineJoin.Round), geo);
    }
}
