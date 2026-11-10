using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CDSMeasure.Views;

namespace CDSMeasure;

public sealed class MainWindow : Window
{
    private readonly ContentControl _host = new();
    private readonly TextBlock _title = new(), _sub = new();
    private readonly CdsButton _back = new(), _settings = new();
    private readonly Seg _sl = new("SL"), _en = new("EN");
    private string _titleKey = "";

    public MainWindow()
    {
        Title = "CDS TapeLab";
        Width = 1360; Height = 820; MinWidth = 1000; MinHeight = 640;
        Background = Ui.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---- glava ----
        var head = new Grid { Margin = new Thickness(24, 14, 24, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            var bmp = new Bitmap(AssetLoader.Open(new Uri("avares://CDSTapeLab/Assets/logo.png")));
            brand.Children.Add(new Image { Source = bmp, Height = 36 });
            Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://CDSTapeLab/Assets/icon.png"))));
        }
        catch { brand.Children.Add(new TextBlock { Text = "CDS TAPELAB", FontFamily = Ui.Head, FontSize = 22, Foreground = Ui.Gold }); }
        _sub.FontSize = 11; _sub.Foreground = Ui.Dim; _sub.VerticalAlignment = VerticalAlignment.Bottom; _sub.Margin = new Thickness(0, 0, 0, 4);
        brand.Children.Add(_sub);
        Grid.SetColumn(brand, 0); head.Children.Add(brand);

        _title.FontFamily = Ui.Head; _title.FontSize = 26; _title.Foreground = Ui.Text;
        _title.HorizontalAlignment = HorizontalAlignment.Center; _title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_title, 1); head.Children.Add(_title);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _back.Click += ShowStart;
        _settings.Click += () => _ = new SettingsWindow().ShowDialog(this);
        _sl.Margin = new Thickness(0); _en.Margin = new Thickness(0);
        _sl.Click += () => Loc.Set("sl");
        _en.Click += () => Loc.Set("en");
        right.Children.Add(_back); right.Children.Add(_settings); right.Children.Add(_sl); right.Children.Add(_en);
        Grid.SetColumn(right, 2); head.Children.Add(right);

        var headBox = new Border { Child = head, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(headBox, 0); root.Children.Add(headBox);
        Grid.SetRow(_host, 1); root.Children.Add(_host);
        Content = root;

        Loc.Changed += ApplyLang;
        Closing += (_, _) => (_host.Content as IDisposable)?.Dispose();
        ShowStart();
        var test = Environment.GetEnvironmentVariable("CDS_OPEN");   // samo za preizkuse: takoj odpri izbrano meritev
        if (!string.IsNullOrEmpty(test)) Open(test);
    }

    private void ShowStart()
    {
        (_host.Content as IDisposable)?.Dispose();
        var v = new StartView();
        v.Selected += Open;
        _host.Content = v;
        _back.IsVisible = false;
        _titleKey = "";
        ApplyLang();
    }

    private void Open(string id)
    {
        (_host.Content as IDisposable)?.Dispose();
        Control v = id switch { "volt" => new VoltView(), "az" => new AzimuthView(), "wfs" => new WfView(), "pb" => new PbView(), "rec" => new RecView(), _ => new StartView() };
        _titleKey = id switch { "wfs" => "tile.wfs.t", "pb" => "tile.pb.t", "az" => "tile.az.t", "volt" => "tile.volt.t", _ => "tile.rec.t" };
        _host.Content = v;
        _back.IsVisible = true;
        ApplyLang();
    }

    private void ApplyLang()
    {
        _sub.Text = Loc.T("app.sub");
        _back.Text = Loc.T("back");
        _settings.Text = Ui.Tt("Nastavitve", "Settings");
        _sl.On = Loc.Lang == "sl"; _en.On = Loc.Lang == "en";
        _title.Text = _titleKey == "" ? Loc.T("start.title") : Loc.T(_titleKey);
    }
}
