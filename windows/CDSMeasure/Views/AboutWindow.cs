using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CDSMeasure;

/// <summary>O programu: različica, licenca, spletna stran in preverjanje posodobitev (brez XAML).</summary>
public sealed class AboutWindow : Window
{
    public const string Website = "https://www.cassettedeck-service.com";
    public const string UpdateUrl = "https://www.cassettedeck-service.com/tapelab.html";
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public const string ManualFile = "CDS_TapeLab_User_Manual.pdf";

    /// <summary>Odpre angleška navodila (PDF), ki so nameščena v mapi Docs ob programu.</summary>
    public static void OpenManual(Window? owner)
    {
        try
        {
            string dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            string path = Path.Combine(dir, "Docs", ManualFile);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
        }
        catch { }
        var r = owner == null
            ? MessageBox.Show(Loc.T("help.missing"), "CDS TapeLab", MessageBoxButton.YesNo, MessageBoxImage.Information)
            : MessageBox.Show(owner, Loc.T("help.missing"), "CDS TapeLab", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (r == MessageBoxResult.Yes) Open(UpdateUrl);
    }

    public static string VersionText()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "" : v.ToString(3);
    }

    public AboutWindow()
    {
        Title = Loc.T("about.btn");
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Res("BgBrush"); Foreground = Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["FontBody"];

        var root = new StackPanel { Margin = new Thickness(28) };
        try
        {
            root.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Height = 56,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12),
            });
        }
        catch { }
        root.Children.Add(new TextBlock { Text = "CDS TapeLab", FontFamily = (FontFamily)Application.Current.Resources["FontHead"], FontSize = 26, Foreground = Res("GoldBrush") });
        root.Children.Add(new TextBlock { Text = Loc.T("app.sub"), Foreground = Res("DimBrush"), Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(Line("about.version", VersionText()));
        root.Children.Add(Line("about.lic", License.IsLicensed ? $"{Loc.T("lic.ok")}: {License.Name}" : "Demo"));
        root.Children.Add(Line("about.web", Website));
        root.Children.Add(Line("about.settings", AppSettings.FilePath));
        root.Children.Add(new TextBlock { Text = "© 2026 Cassette Deck Service, Ljubljana", Foreground = Res("DimBrush"), FontSize = 12, Margin = new Thickness(0, 14, 0, 0) });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var upd = new Button { Content = Loc.T("about.update"), Style = (Style)Application.Current.Resources["CdsPrimary"], MinWidth = 150 };
        var man = new Button { Content = Loc.T("help.btn"), Style = (Style)Application.Current.Resources["CdsButton"], Margin = new Thickness(8, 0, 0, 0) };
        var web = new Button { Content = Loc.T("about.web"), Style = (Style)Application.Current.Resources["CdsButton"], Margin = new Thickness(8, 0, 0, 0) };
        var close = new Button { Content = Loc.T("about.close"), Style = (Style)Application.Current.Resources["CdsButton"], Margin = new Thickness(8, 0, 0, 0) };
        upd.Click += (_, _) => Open(UpdateUrl);
        web.Click += (_, _) => Open(Website);
        man.Click += (_, _) => OpenManual(this);
        close.Click += (_, _) => Close();
        bar.Children.Add(upd); bar.Children.Add(man); bar.Children.Add(web); bar.Children.Add(close);
        root.Children.Add(bar);
        root.Children.Add(new TextBlock { Text = Loc.T("about.update.hint"), Foreground = Res("DimBrush"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = root;
    }

    private static UIElement Line(string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = Loc.T(key), Foreground = Res("DimBrush") };
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        return g;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
