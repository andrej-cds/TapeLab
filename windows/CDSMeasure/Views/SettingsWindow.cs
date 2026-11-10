using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CDSMeasure.Core;
using Microsoft.Win32;
using NAudio.Wave;

namespace CDSMeasure;

/// <summary>Nastavitve: zvočna kartica (ASIO), vhodni/izhodni kanali, vzorčenje, nivo sweepa in mapa za shranjevanje.
/// Okno je brez XAML (kot LicenseWindow). Spremembe veljajo od naslednje meritve.</summary>
public sealed class SettingsWindow : Window
{
    private readonly ComboBox _drv = Combo(), _in = Combo(), _out = Combo(), _rate = Combo(), _rep = Combo();
    private readonly TextBox _level = new() { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _folder = new() { IsReadOnly = true };
    private readonly TextBlock _msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private int _inCount = 8, _outCount = 8;
    private bool _loading = true;

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static ComboBox Combo() => new()
    {
        Foreground = Brushes.Black, Background = Brushes.White, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public SettingsWindow()
    {
        Title = Loc.T("set.title");
        Width = 640; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Res("BgBrush"); Foreground = Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["FontBody"];
        foreach (var tb in new[] { _level, _folder })
        {
            tb.Background = Res("PanelBrush"); tb.Foreground = Res("TextBrush");
            tb.BorderBrush = Res("LineBrush"); tb.Padding = new Thickness(6, 4, 6, 4); tb.MinHeight = 28;
        }

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(Group("set.audio"));
        root.Children.Add(Row("set.driver", _drv));
        root.Children.Add(Row("set.rate", _rate));
        var inRow = new DockPanel();
        var bTest = Btn("set.test", false); bTest.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(bTest, Dock.Right);
        inRow.Children.Add(bTest); inRow.Children.Add(_in);
        root.Children.Add(Row("set.in", inRow));
        root.Children.Add(Row("set.out", _out));
        root.Children.Add(Group("set.signal"));
        var lvl = new StackPanel();
        lvl.Children.Add(_level);
        lvl.Children.Add(new TextBlock { Text = Loc.T("set.level.hint"), TextWrapping = TextWrapping.Wrap, Foreground = Res("DimBrush"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        root.Children.Add(Row("set.level", lvl));
        root.Children.Add(Group("set.files"));

        var fb = new DockPanel();
        var bDef = Btn("set.default", false); var bOpen = Btn("set.open", false); var bBrowse = Btn("set.browse", false);
        DockPanel.SetDock(bDef, Dock.Right); DockPanel.SetDock(bOpen, Dock.Right); DockPanel.SetDock(bBrowse, Dock.Right);
        bDef.Margin = new Thickness(6, 0, 0, 0); bOpen.Margin = new Thickness(6, 0, 0, 0); bBrowse.Margin = new Thickness(6, 0, 0, 0);
        fb.Children.Add(bDef); fb.Children.Add(bOpen); fb.Children.Add(bBrowse); fb.Children.Add(_folder);
        root.Children.Add(Row("set.folder", fb));
        root.Children.Add(Row("set.report", _rep));

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var ok = Btn("set.save", true); var cancel = Btn("set.cancel", false); cancel.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(ok); bar.Children.Add(cancel);
        root.Children.Add(bar);
        root.Children.Add(new TextBlock { Text = Loc.T("set.note"), Foreground = Res("DimBrush"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        root.Children.Add(_msg);
        Content = root;

        // vrednosti
        _drv.Items.Add(new ComboBoxItem { Content = Loc.T("set.auto"), Tag = "" });
        foreach (var n in AsioCapture.Drivers()) _drv.Items.Add(new ComboBoxItem { Content = Nice(n), Tag = n });
        SelectByTag(_drv, AppSettings.AsioDriver);
        _rate.Items.Add(new ComboBoxItem { Content = Loc.T("set.rate.auto"), Tag = 0 });
        foreach (int r in new[] { 192000, 96000, 48000 })
            _rate.Items.Add(new ComboBoxItem { Content = $"{r / 1000} kHz", Tag = r });
        SelectByTag(_rate, AppSettings.SampleRate);
        _rep.Items.Add(new ComboBoxItem { Content = Loc.T("set.rep.dark"), Tag = 0 });
        _rep.Items.Add(new ComboBoxItem { Content = Loc.T("set.rep.light"), Tag = 1 });
        SelectByTag(_rep, AppSettings.ReportLight ? 1 : 0);
        _level.Text = AppSettings.SweepRefDbfs.ToString("0.#", CultureInfo.InvariantCulture);
        _folder.Text = string.IsNullOrWhiteSpace(AppSettings.SaveDir) ? AppSettings.DefaultDir : AppSettings.SaveDir;

        Probe(AppSettings.InputStart, AppSettings.OutputStart);
        _loading = false;
        _drv.SelectionChanged += (_, _) => { if (!_loading) Probe(SelStart(_in), SelStart(_out)); };

        bDef.Click += (_, _) => _folder.Text = AppSettings.DefaultDir;
        bOpen.Click += (_, _) => OpenFolder();
        bBrowse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("set.folder"), InitialDirectory = Directory.Exists(_folder.Text) ? _folder.Text : AppSettings.DefaultDir };
            if (dlg.ShowDialog(this) == true) _folder.Text = dlg.FolderName;
        };
        bTest.Click += (_, _) => RunInputTest();
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) => Apply();
    }

    private UIElement Group(string key) => new TextBlock
    {
        Text = Loc.T(key), Foreground = Res("GoldBrush"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6),
    };

    private UIElement Row(string key, UIElement ctl)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = Loc.T(key), Foreground = Res("DimBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 10, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(ctl, 1);
        g.Children.Add(l); g.Children.Add(ctl);
        return g;
    }

    private static Button Btn(string key, bool primary) => new()
    {
        Content = Loc.T(key), MinWidth = primary ? 110 : 0,
        Style = (Style)Application.Current.Resources[primary ? "CdsPrimary" : "CdsButton"],
    };

    private static void SelectByTag(ComboBox cb, object tag)
    {
        foreach (ComboBoxItem it in cb.Items)
            if (Equals(it.Tag, tag)) { cb.SelectedItem = it; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private static int SelStart(ComboBox cb) => cb.SelectedItem is ComboBoxItem { Tag: int t } ? t : 0;

    /// <summary>Prijazno ime ASIO gonilnika: opis iz registra, sicer ime datoteke gonilnika, sicer nevtralno ime.</summary>
    private static string Nice(string n)
    {
        string? d = null;
        try
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                using var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                using var k = hk.OpenSubKey(@"SOFTWARE\ASIO\" + n);
                if (k == null) continue;
                d = (k.GetValue("Description") as string)?.Trim();
                if (string.IsNullOrEmpty(d) && k.GetValue("CLSID") is string clsid)
                {
                    using var c = hk.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + clsid);
                    d = (c?.GetValue(null) as string)?.Trim();
                    if (string.IsNullOrEmpty(d) || d.Equals(n, StringComparison.OrdinalIgnoreCase))
                    {
                        using var ip = c?.OpenSubKey("InprocServer32");
                        if (ip?.GetValue(null) is string dll && System.IO.File.Exists(Environment.ExpandEnvironmentVariables(dll)))
                        {
                            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ExpandEnvironmentVariables(dll));
                            d = string.IsNullOrWhiteSpace(vi.ProductName) ? vi.FileDescription?.Trim() : vi.ProductName.Trim();
                        }
                    }
                }
                if (!string.IsNullOrEmpty(d)) break;
            }
        }
        catch { }
        if (!string.IsNullOrEmpty(d) && !d.Equals(n, StringComparison.OrdinalIgnoreCase)) return d + " (" + n + ")";
        return n.StartsWith("cmasio", StringComparison.OrdinalIgnoreCase) ? "C-Media (" + n + ")" : n;
    }

    private string SelDriver() => _drv.SelectedItem is ComboBoxItem { Tag: string s } ? s : "";

    /// <summary>Prebere število kanalov izbranega gonilnika (le če zajem ne teče) in napolni izbiro parov.</summary>
    private void Probe(int inStart, int outStart)
    {
        _inCount = _outCount = 8; _msg.Text = "";
        if (AsioCapture.IsActive) _msg.Text = Loc.T("set.busy");
        else
        {
            string drv = SelDriver();
            string? name = drv != "" ? drv : AsioCapture.FindAuto();
            if (name != null)
            {
                try
                {
                    using var a = new AsioOut(name);
                    _inCount = Math.Max(1, a.DriverInputChannelCount);
                    _outCount = Math.Max(1, a.DriverOutputChannelCount);
                }
                catch (Exception ex) { _msg.Text = Loc.T("set.noprobe") + "  [" + name + ": " + ex.Message + "]"; }
            }
        }
        Fill(_in, _inCount, inStart); Fill(_out, _outCount, outStart);
    }

    private static void Fill(ComboBox cb, int count, int sel)
    {
        cb.Items.Clear();
        if (count < 2) cb.Items.Add(new ComboBoxItem { Content = "1", Tag = 0 });
        for (int s = 0; s + 1 < count; s += 2)
            cb.Items.Add(new ComboBoxItem { Content = $"{s + 1} + {s + 2}", Tag = s });
        SelectByTag(cb, sel);
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_folder.Text);
            Process.Start(new ProcessStartInfo(_folder.Text) { UseShellExecute = true });
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    /// <summary>Test vhoda: raven na vseh vhodnih kanalih izbranega gonilnika (za iskanje para s signalom).</summary>
    private void RunInputTest()
    {
        if (AsioCapture.IsActive) { Fail(Loc.T("set.busy")); return; }
        string drv = SelDriver();
        string? name = drv != "" ? drv : AsioCapture.FindAuto();
        if (name == null) { Fail(Loc.T("wfs.noasio")); return; }
        int rate = _rate.SelectedItem is ComboBoxItem { Tag: int r } ? r : 0;
        _msg.Text = "";
        new InputTestWindow(name, rate) { Owner = this }.ShowDialog();
    }

    private void Fail(string text) { _msg.Foreground = Res("RedBrush"); _msg.Text = text; }

    private void Apply()
    {
        if (!double.TryParse(_level.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double lv) || lv < -24 || lv > -6)
        { Fail(Loc.T("set.invalid")); return; }
        string dir = _folder.Text.Trim();
        try { Directory.CreateDirectory(dir); }
        catch { Fail(Loc.T("set.badfolder")); return; }

        AppSettings.AsioDriver = SelDriver();
        AppSettings.SampleRate = _rate.SelectedItem is ComboBoxItem { Tag: int r } ? r : 0;
        AppSettings.InputStart = SelStart(_in);
        AppSettings.OutputStart = SelStart(_out);
        AppSettings.SweepRefDbfs = lv;
        AppSettings.ReportLight = _rep.SelectedItem is ComboBoxItem { Tag: 1 };
        AppSettings.SaveDir = string.Equals(dir, AppSettings.DefaultDir, StringComparison.OrdinalIgnoreCase) ? "" : dir;
        AppSettings.Save();
        Close();
    }
}
