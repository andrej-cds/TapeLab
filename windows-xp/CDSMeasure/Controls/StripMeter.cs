using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CDSMeasure.Controls;

/// <summary>Vodoravni analogni trak s kazalcem čez celo širino (za hitrost in W&amp;F).</summary>
public class StripMeter : FrameworkElement
{
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Mono = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush FaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)));
    private static readonly Brush FillBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E)));
    private static readonly Brush GoldBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xA9, 0x6E)));
    private static readonly Brush LightBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xBC, 0x85)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C)));
    private static readonly Brush RedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0x00, 0x1B)));
    private static readonly Pen FacePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x22))), 1));
    private static readonly Pen BasePen = Frozen(new Pen(GoldBrush, 1.5));
    private static readonly Pen MajorPen = Frozen(new Pen(LightBrush, 1.6));
    private static readonly Pen MinorPen = Frozen(new Pen(DimBrush, 1));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xED, 0xE8)));
    private static readonly Pen ZeroPen = Frozen(new Pen(LightBrush, 3.6));
    private static readonly Typeface MonoBold = new(new FontFamily("JetBrains Mono, Consolas"),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Pen NeedlePen = Frozen(new Pen(RedBrush, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static DependencyProperty Reg<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(StripMeter),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), 0.0);
    public static readonly DependencyProperty MinimumProperty = Reg(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Reg(nameof(Maximum), 1.0);
    public static readonly DependencyProperty MajorStepProperty = Reg(nameof(MajorStep), 0.1);
    public static readonly DependencyProperty MinorPerMajorProperty = Reg(nameof(MinorPerMajor), 5);
    public static readonly DependencyProperty LabelFormatProperty = Reg(nameof(LabelFormat), "0.##");
    public static readonly DependencyProperty CaptionProperty = Reg(nameof(Caption), "");
    public static readonly DependencyProperty LimitProperty = Reg(nameof(Limit), double.NaN);
    public static readonly DependencyProperty LimitLabelProperty = Reg(nameof(LimitLabel), "");

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double MajorStep { get => (double)GetValue(MajorStepProperty); set => SetValue(MajorStepProperty, value); }
    public int MinorPerMajor { get => (int)GetValue(MinorPerMajorProperty); set => SetValue(MinorPerMajorProperty, value); }
    public string LabelFormat { get => (string)GetValue(LabelFormatProperty); set => SetValue(LabelFormatProperty, value); }
    /// <summary>Tovarniški maksimum (NaN = ni označen).</summary>
    public double Limit { get => (double)GetValue(LimitProperty); set => SetValue(LimitProperty, value); }
    public string LimitLabel { get => (string)GetValue(LimitLabelProperty); set => SetValue(LimitLabelProperty, value); }
    private static readonly Brush LimitZoneBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0x00, 0x1B)));
    private static readonly Pen LimitPen = Frozen(new Pen(TextBrush, 1.6) { DashStyle = DashStyles.Dash });
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 50 || Maximum <= Minimum || MajorStep <= 0) return;

        double ppd = Dpi.Get(this).PixelsPerDip;
        dc.DrawRoundedRectangle(FaceBrush, FacePen, new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);

        double left = 34, right = w - 34;
        double baseY = h * 0.56;
        double X(double v) => left + (MathX.Clamp(v, Minimum, Maximum) - Minimum) / (Maximum - Minimum) * (right - left);

        // polnilo od nule (ali od minimuma) do vrednosti
        double zero = Minimum < 0 ? 0 : Minimum;
        double x0 = X(zero), x1 = X(Value);
        dc.DrawRectangle(FillBrush, null, new Rect(Math.Min(x0, x1), baseY - 6, Math.Abs(x1 - x0), 12));

        // skala
        dc.DrawLine(BasePen, new Point(left, baseY), new Point(right, baseY));
        int n = (int)Math.Round((Maximum - Minimum) / MajorStep);
        int minor = Math.Max(1, MinorPerMajor);
        for (int i = 0; i <= n; i++)
        {
            double v = Minimum + i * MajorStep;
            double x = X(v);
            bool isZero = Minimum < 0 && Maximum > 0 && Math.Abs(v) < MajorStep * 1e-6;
            if (isZero)
                dc.DrawLine(ZeroPen, new Point(x, baseY - 21), new Point(x, baseY + 15));   // debelejša in daljša črtica
            else
                dc.DrawLine(MajorPen, new Point(x, baseY - 13), new Point(x, baseY + 13));

            string text = v.ToString(LabelFormat, CultureInfo.CurrentCulture);
            var ft = isZero
                ? FT.New(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoBold, 15, LightBrush, ppd)
                : FT.New(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 12, LightBrush, ppd);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + (isZero ? 17 : 16)));

            if (i < n)
            {
                for (int j = 1; j < minor; j++)
                {
                    double xm = X(v + j * MajorStep / minor);
                    dc.DrawLine(MinorPen, new Point(xm, baseY - 6), new Point(xm, baseY + 6));
                }
            }
        }

        // napis v kotu
        if (!string.IsNullOrEmpty(Caption))
        {
            var cap = FT.New(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Mono, 11, DimBrush, ppd);
            dc.DrawText(cap, new Point(14, 8));
        }

        // tovarniški maksimum: zasenčeno območje nad mejo + črtkana črta z oznako
        if (!double.IsNaN(Limit) && Limit > Minimum && Limit < Maximum)
        {
            double lx = X(Limit);
            dc.DrawRectangle(LimitZoneBrush, null, new Rect(lx, baseY - 30, right - lx, 52));
            dc.DrawLine(LimitPen, new Point(lx, baseY - 34), new Point(lx, baseY + 22));
            if (!string.IsNullOrEmpty(LimitLabel))
            {
                var lt = FT.New(LimitLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoBold, 11, TextBrush, ppd);
                double tx = lx + 6 + lt.Width > w - 8 ? lx - 6 - lt.Width : lx + 6;
                dc.DrawText(lt, new Point(tx, 8));
            }
        }

        // kazalec
        double nx = X(Value);
        dc.DrawLine(NeedlePen, new Point(nx, baseY - 36), new Point(nx, baseY + 12));
        dc.DrawEllipse(RedBrush, null, new Point(nx, baseY - 38), 4.5, 4.5);
    }
}
