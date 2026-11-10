using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CDSMeasure.Core;

namespace CDSMeasure.Controls;

/// <summary>Vodoravni analogni trak voltmetra čez celo širino z enim kazalcem (barva kanala).
/// Merjena (simetrična) skala ima nazivno vrednost na sredini traku; standardna skala gre od 0 do oznake.</summary>
public class VoltStrip : FrameworkElement
{
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Mono = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MonoBold = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush FaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)));
    private static readonly Brush GoldBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xA9, 0x6E)));
    private static readonly Brush LightBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xBC, 0x85)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xED, 0xE8)));
    private static readonly Brush RedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0x00, 0x1B)));
    private static readonly Pen FacePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x22))), 1));
    private static readonly Pen SelPen = Frozen(new Pen(GoldBrush, 2));
    private static readonly Pen BasePen = Frozen(new Pen(GoldBrush, 1.5));
    private static readonly Pen MajorPen = Frozen(new Pen(LightBrush, 1.6));
    private static readonly Pen MinorPen = Frozen(new Pen(DimBrush, 1));
    private static readonly Pen MarkPen = Frozen(new Pen(LightBrush, 3.6));

    private static DependencyProperty Reg<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(VoltStrip),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), 0.0);
    public static readonly DependencyProperty RangeProperty = Reg<VoltRange?>(nameof(Range), null);
    public static readonly DependencyProperty NeedleBrushProperty = Reg<Brush>(nameof(NeedleBrush), RedBrush);
    public static readonly DependencyProperty CaptionProperty = Reg(nameof(Caption), "");
    public static readonly DependencyProperty SelectedProperty = Reg(nameof(Selected), false);

    /// <summary>Izmerjena napetost (V RMS).</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public VoltRange? Range { get => (VoltRange?)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public Brush NeedleBrush { get => (Brush)GetValue(NeedleBrushProperty); set => SetValue(NeedleBrushProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    /// <summary>Kanal je izbran za nastavljanje območja (zlat okvir).</summary>
    public bool Selected { get => (bool)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var r = Range;
        if (r == null || w < 120 || h < 70) return;

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRoundedRectangle(FaceBrush, Selected ? SelPen : FacePen, new Rect(1, 1, w - 2, h - 2), 8, 8);

        double left = 40, right = w - 40;
        double baseY = h * 0.60;
        double X(double v) => left + Math.Clamp(v, 0, r.MaxV) / r.MaxV * (right - left);
        var needle = new Pen(NeedleBrush, 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        // podlaga skale
        dc.DrawLine(BasePen, new Point(left, baseY), new Point(right, baseY));
        int n = (int)Math.Round(r.MaxV / r.StepV);
        int minor = Math.Max(1, r.MinorPerMajor);
        for (int i = 0; i <= n; i++)
        {
            double v = i * r.StepV;
            double x = X(v);
            bool isMark = r.HasMark && Math.Abs(v - r.MarkV) < r.StepV * 1e-6;
            if (isMark)
                dc.DrawLine(MarkPen, new Point(x, baseY - 26), new Point(x, baseY + 18));
            else
                dc.DrawLine(MajorPen, new Point(x, baseY - 14), new Point(x, baseY + 14));

            string text = (v / r.Div).ToString("0.###", CultureInfo.InvariantCulture);
            var ft = isMark
                ? new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 17, TextBrush, ppd)
                : new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 12, LightBrush, ppd);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + (isMark ? 21 : 17)));

            if (i < n)
                for (int j = 1; j < minor; j++)
                {
                    double xm = X(v + j * r.StepV / minor);
                    dc.DrawLine(MinorPen, new Point(xm, baseY - 6), new Point(xm, baseY + 6));
                }
        }

        // napis kanala in enota
        var cap = new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 20, NeedleBrush, ppd);
        dc.DrawText(cap, new Point(16, 8));
        var unit = new FormattedText(r.Unit + "  (" + r.Name + ")", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 12, DimBrush, ppd);
        dc.DrawText(unit, new Point(w - 16 - unit.Width, 12));

        // kazalec
        double val = Value;
        double nx = X(val);
        dc.DrawLine(needle, new Point(nx, baseY - 42), new Point(nx, baseY + 12));
        dc.DrawEllipse(NeedleBrush, null, new Point(nx, baseY - 44), 5, 5);

        if (val > r.MaxV)
        {
            var over = new FormattedText("OVER ►", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 14, RedBrush, ppd);
            dc.DrawText(over, new Point(right - over.Width, baseY - 62));
        }
    }
}
