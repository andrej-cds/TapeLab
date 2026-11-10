using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CDSMeasure.Views;

/// <summary>Začasen zaslon za meritve, ki še niso narejene.</summary>
public class SoonView : UserControl, IDisposable
{
    private readonly TextBlock _text = new()
    {
        FontFamily = new FontFamily("pack://application:,,,/Assets/Fonts/#Vipnagorgialla"),
        FontSize = 28,
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x95, 0x8C)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public SoonView()
    {
        Content = _text;
        Loc.Changed += Apply;
        Apply();
    }

    private void Apply() => _text.Text = Loc.T("soon");
    public void Dispose() => Loc.Changed -= Apply;
}
