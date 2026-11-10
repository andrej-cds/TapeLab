using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CDSMeasure;

/// <summary>Okno za vnos licence in prikaz ID-ja računalnika (brez XAML, da je manj možnosti za napake).</summary>
public sealed class LicenseWindow : Window
{
    private readonly TextBox _key = new() { Height = 90, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox _mid = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 18, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Button _ok = new() { MinWidth = 120, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

    public LicenseWindow()
    {
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["BgBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        FontFamily = (FontFamily)Application.Current.Resources["FontBody"];
        _ok.Style = (Style)Application.Current.Resources["CdsPrimary"];
        _mid.Text = License.MachineId();
        _mid.Background = (Brush)Application.Current.Resources["PanelBrush"];
        _mid.Foreground = (Brush)Application.Current.Resources["GoldBrush"];
        _key.Background = (Brush)Application.Current.Resources["PanelBrush"];
        _key.Foreground = (Brush)Application.Current.Resources["TextBrush"];

        var lblId = new TextBlock(); var lblKey = new TextBlock { Margin = new Thickness(0, 12, 0, 4) };
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(_info); sp.Children.Add(lblId); sp.Children.Add(_mid);
        sp.Children.Add(lblKey); sp.Children.Add(_key); sp.Children.Add(_ok); sp.Children.Add(_status);
        Content = sp;

        void Texts()
        {
            Title = Loc.T("lic.title");
            lblId.Text = Loc.T("lic.mid");
            lblKey.Text = Loc.T("lic.key");
            _ok.Content = Loc.T("lic.activate");
            _info.Text = License.IsLicensed ? $"{Loc.T("lic.ok")}: {License.Name}" : Loc.T("lic.demo");
            _info.Foreground = (Brush)Application.Current.Resources[License.IsLicensed ? "GoldBrush" : "DimBrush"];
        }
        Texts();
        _ok.Click += (_, _) =>
        {
            string? err = License.Activate(_key.Text);
            _status.Foreground = (Brush)Application.Current.Resources[err == null ? "GoldBrush" : "RedBrush"];
            _status.Text = err == null ? Loc.T("lic.activated") : Loc.T(err);
            Texts();
        };
    }
}
