using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CDSMeasure.Controls;

/// <summary>Vodoravni analogni trak s kazalcem čez celo širino (hitrost in W&amp;F).</summary>
public sealed class StripMeter : Control
{
    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface MonoBold = new(Ui.Mono, FontStyle.Normal, FontWeight.Bold);
    private static readonly IBrush Face = Brush.Parse("#0F0F0F");
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E));
    private static readonly IBrush LimitZone = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0x00, 0x1B));
    private static readonly Pen FacePen = new(Ui.Line, 1);
    private static readonly Pen BasePen = new(Ui.Gold, 1.5);
    private static readonly Pen MajorPen = new(Ui.GoldLight, 1.6);
    private static readonly Pen MinorPen = new(Ui.Dim, 1);
    private static readonly Pen ZeroPen = new(Ui.GoldLight, 3.6);
    private static readonly Pen NeedlePen = new(Ui.Red, 2.6, lineCap: PenLineCap.Round);

    private double _value, _min, _max = 1, _major = 0.1;
    private int _minor = 5;
    private string _fmt = "0.##", _caption = "";

    public double Value { get => _value; set { _value = value; InvalidateVisual(); } }
    public double Minimum { get => _min; set { _min = value; InvalidateVisual(); } }
    public double Maximum { get => _max; set { _max = value; InvalidateVisual(); } }
    public double MajorStep { get => _major; set { _major = value; InvalidateVisual(); } }
    public int MinorPerMajor { get => _minor; set { _minor = value; InvalidateVisual(); } }
    public string LabelFormat { get => _fmt; set { _fmt = value; InvalidateVisual(); } }
    public string Caption { get => _caption; set { _caption = value; InvalidateVisual(); } }

    private static FormattedText Txt(string s, Typeface tf, double size, IBrush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, b);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 80 || h < 50 || _max <= _min || _major <= 0) return;
        dc.DrawRectangle(Face, FacePen, new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);

        double left = 34, right = w - 34, baseY = h * 0.56;
        double X(double v) => left + (Math.Clamp(v, _min, _max) - _min) / (_max - _min) * (right - left);

        double zero = _min < 0 ? 0 : _min;
        double x0 = X(zero), x1 = X(_value);
        dc.DrawRectangle(Fill, null, new Rect(Math.Min(x0, x1), baseY - 6, Math.Abs(x1 - x0), 12));

        dc.DrawLine(BasePen, new Point(left, baseY), new Point(right, baseY));
        int n = (int)Math.Round((_max - _min) / _major);
        int minor = Math.Max(1, _minor);
        for (int i = 0; i <= n; i++)
        {
            double v = _min + i * _major;
            double x = X(v);
            bool isZero = _min < 0 && _max > 0 && Math.Abs(v) < _major * 1e-6;
            if (isZero) dc.DrawLine(ZeroPen, new Point(x, baseY - 21), new Point(x, baseY + 15));
            else dc.DrawLine(MajorPen, new Point(x, baseY - 13), new Point(x, baseY + 13));
            var ft = isZero ? Txt(v.ToString(_fmt, CultureInfo.InvariantCulture), MonoBold, 15, Ui.GoldLight)
                            : Txt(v.ToString(_fmt, CultureInfo.InvariantCulture), Mono, 12, Ui.GoldLight);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + (isZero ? 17 : 16)));
            if (i < n)
                for (int j = 1; j < minor; j++)
                {
                    double xm = X(v + j * _major / minor);
                    dc.DrawLine(MinorPen, new Point(xm, baseY - 6), new Point(xm, baseY + 6));
                }
        }
        if (_caption.Length > 0) dc.DrawText(Txt(_caption, Mono, 11, Ui.Dim), new Point(14, 8));

        double nx = X(_value);
        dc.DrawLine(NeedlePen, new Point(nx, baseY - 36), new Point(nx, baseY + 12));
        dc.DrawEllipse(Ui.Red, null, new Point(nx, baseY - 38), 4.5, 4.5);
    }
}
