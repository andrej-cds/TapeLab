using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CDSMeasure.Core;

namespace CDSMeasure.Controls;

/// <summary>Vodoravni analogni trak voltmetra čez celo širino z enim kazalcem (barva kanala).
/// Merjena (simetrična) skala ima nazivno vrednost na sredini traku; standardna skala gre od 0 do oznake.</summary>
public sealed class VoltStrip : Control
{
    private static readonly IBrush Face = Brush.Parse("#0F0F0F");
    private static readonly Typeface Mono = new(Ui.Mono, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface MonoBold = new(Ui.Mono, FontStyle.Normal, FontWeight.Bold);
    private static readonly Pen FacePen = new(Ui.Line, 1);
    private static readonly Pen SelPen = new(Ui.Gold, 2);
    private static readonly Pen BasePen = new(Ui.Gold, 1.5);
    private static readonly Pen MajorPen = new(Ui.GoldLight, 1.6);
    private static readonly Pen MinorPen = new(Ui.Dim, 1);
    private static readonly Pen MarkPen = new(Ui.GoldLight, 3.6);

    private double _value; private VoltRange? _range; private IBrush _needle = Ui.Red; private string _caption = ""; private bool _selected;

    /// <summary>Izmerjena napetost (V RMS).</summary>
    public double Value { get => _value; set { _value = value; InvalidateVisual(); } }
    public VoltRange? Range { get => _range; set { _range = value; InvalidateVisual(); } }
    public IBrush NeedleBrush { get => _needle; set { _needle = value; InvalidateVisual(); } }
    public string Caption { get => _caption; set { _caption = value; InvalidateVisual(); } }
    /// <summary>Kanal je izbran za nastavljanje območja (zlat okvir).</summary>
    public bool Selected { get => _selected; set { _selected = value; InvalidateVisual(); } }

    private static FormattedText Txt(string s, Typeface tf, double size, IBrush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, b);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var r = _range;
        if (r == null || w < 120 || h < 70) return;

        dc.DrawRectangle(Face, _selected ? SelPen : FacePen, new Rect(1, 1, w - 2, h - 2), 8, 8);

        double left = 40, right = w - 40;
        double baseY = h * 0.60;
        double X(double v) => left + Math.Clamp(v, 0, r.MaxV) / r.MaxV * (right - left);
        var needle = new Pen(_needle, 3.2, lineCap: PenLineCap.Round);

        dc.DrawLine(BasePen, new Point(left, baseY), new Point(right, baseY));
        int n = (int)Math.Round(r.MaxV / r.StepV);
        int minor = Math.Max(1, r.MinorPerMajor);
        for (int i = 0; i <= n; i++)
        {
            double v = i * r.StepV;
            double x = X(v);
            bool isMark = r.HasMark && Math.Abs(v - r.MarkV) < r.StepV * 1e-6;
            if (isMark) dc.DrawLine(MarkPen, new Point(x, baseY - 26), new Point(x, baseY + 18));
            else dc.DrawLine(MajorPen, new Point(x, baseY - 14), new Point(x, baseY + 14));

            string text = (v / r.Div).ToString("0.###", CultureInfo.InvariantCulture);
            var ft = isMark ? Txt(text, MonoBold, 17, Ui.Text) : Txt(text, Mono, 12, Ui.GoldLight);
            dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + (isMark ? 21 : 17)));

            if (i < n)
                for (int j = 1; j < minor; j++)
                {
                    double xm = X(v + j * r.StepV / minor);
                    dc.DrawLine(MinorPen, new Point(xm, baseY - 6), new Point(xm, baseY + 6));
                }
        }

        var cap = Txt(_caption, MonoBold, 20, _needle);
        dc.DrawText(cap, new Point(16, 8));
        var unit = Txt(r.Unit + "  (" + r.Name + ")", Mono, 12, Ui.Dim);
        dc.DrawText(unit, new Point(w - 16 - unit.Width, 12));

        double val = _value;
        double nx = X(val);
        dc.DrawLine(needle, new Point(nx, baseY - 42), new Point(nx, baseY + 12));
        dc.DrawEllipse(_needle, null, new Point(nx, baseY - 44), 5, 5);

        if (val > r.MaxV)
        {
            var over = Txt("OVER ►", MonoBold, 14, Ui.Red);
            dc.DrawText(over, new Point(right - over.Width, baseY - 62));
        }
    }
}
