using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CDSMeasure.Audio;

namespace CDSMeasure.Views;

/// <summary>Nastavitve zvočne kartice: naprava, prvi vhodni kanal, vzorčenje + preizkus vhoda (raven na vseh kanalih).</summary>
public sealed class SettingsWindow : Window
{
    private readonly ComboBox _dev = new(), _pair = new(), _rate = new();
    private readonly TextBlock _info = new(), _test = new();
    private readonly CdsButton _testBtn = new();
    private readonly List<AudioDevice> _devices = PaEngine.Inputs();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private PaCapture? _cap;
    private float[] _peak = Array.Empty<float>();
    private readonly object _lock = new();

    public SettingsWindow()
    {
        Title = Ui.Tt("Nastavitve – CDS TapeLab", "Settings – CDS TapeLab");
        Width = 620; Height = 640;
        Background = Ui.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var st = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        st.Children.Add(new TextBlock { Text = Ui.Tt("Zvočna kartica", "Sound card"), FontFamily = Ui.Head, FontSize = 22, Foreground = Ui.Text });

        st.Children.Add(Ui.Label(Ui.Tt("Vhodna naprava (" + PaEngine.ApiName + ")", "Input device (" + PaEngine.ApiName + ")")));
        _dev.HorizontalAlignment = HorizontalAlignment.Stretch;
        var names = new List<string> { Ui.Tt("Samodejno (Behringer / UMC, sicer privzeta)", "Automatic (Behringer / UMC, otherwise default)") };
        names.AddRange(_devices.Select(d => d.Name));
        _dev.ItemsSource = names;
        int di = string.IsNullOrEmpty(AppSettings.AsioDriver) ? 0 : _devices.FindIndex(d => d.Name == AppSettings.AsioDriver) + 1;
        _dev.SelectedIndex = Math.Max(0, di);
        _dev.SelectionChanged += (_, _) => FillPairs();
        st.Children.Add(_dev);

        st.Children.Add(Ui.Label(Ui.Tt("Vhodna kanala (L, D)", "Input channels (L, R)")));
        _pair.HorizontalAlignment = HorizontalAlignment.Stretch;
        st.Children.Add(_pair);

        st.Children.Add(Ui.Label(Ui.Tt("Vzorčenje", "Sample rate")));
        _rate.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rate.ItemsSource = new[] { Ui.Tt("Samodejno (192, 96, 48 kHz)", "Automatic (192, 96, 48 kHz)"), "48 kHz", "96 kHz", "192 kHz" };
        _rate.SelectedIndex = AppSettings.SampleRate switch { 48000 => 1, 96000 => 2, 192000 => 3, _ => 0 };
        st.Children.Add(_rate);

        _info.Foreground = Ui.Dim; _info.FontSize = 12; _info.TextWrapping = TextWrapping.Wrap;
        st.Children.Add(_info);

        // preizkus vhoda
        st.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 6) });
        st.Children.Add(Ui.Label(Ui.Tt("Preizkus vhoda: raven na vseh vhodnih kanalih. Pošlji signal in poglej, kje se pojavi.",
                                       "Input test: level on all input channels. Send a signal and see where it shows up."), 12));
        _testBtn.Click += ToggleTest;
        _testBtn.HorizontalAlignment = HorizontalAlignment.Left;
        st.Children.Add(_testBtn);
        _test.FontFamily = Ui.Mono; _test.FontSize = 12; _test.Foreground = Ui.Text;
        st.Children.Add(_test);
        if (OperatingSystem.IsMacOS())
            st.Children.Add(Ui.Label(Ui.Tt("Če je raven povsod −inf dB: macOS zahteva dovoljenje za mikrofon (Sistemske nastavitve → Zasebnost in varnost → Mikrofon → CDS TapeLab).",
                                           "If every level shows −inf dB: macOS needs microphone permission (System Settings → Privacy & Security → Microphone → CDS TapeLab)."), 12, Ui.Gold));

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var save = new CdsButton(true) { Text = Ui.Tt("Shrani", "Save") };
        var cancel = new CdsButton { Text = Ui.Tt("Prekliči", "Cancel") };
        save.Click += () => { Save(); Close(); };
        cancel.Click += Close;
        btns.Children.Add(cancel); btns.Children.Add(save);
        st.Children.Add(btns);

        Content = new ScrollViewer { Content = st };
        _timer.Tick += (_, _) => ShowLevels();
        Closed += (_, _) => { _timer.Stop(); _cap?.Stop(); };
        FillPairs();
        _testBtn.Text = Ui.Tt("Zaženi preizkus", "Start test");
    }

    private AudioDevice? Chosen()
    {
        int i = _dev.SelectedIndex;
        if (i > 0 && i - 1 < _devices.Count) return _devices[i - 1];
        return PaEngine.Resolve();
    }

    private void FillPairs()
    {
        var d = Chosen();
        var items = new List<string>();
        if (d != null)
        {
            for (int c = 0; c < d.MaxIn; c++)
                items.Add(c + 2 <= d.MaxIn ? $"{c + 1} + {c + 2}" : $"{c + 1}");
            _info.Text = $"{d.Name}: {d.MaxIn} " + Ui.Tt("vhodnih kanalov", "input channels") + ", " +
                         Ui.Tt("privzeto vzorčenje ", "default rate ") + (d.DefaultRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
        }
        else _info.Text = Ui.Tt("Ni zaznane vhodne naprave. " + PaEngine.LastError, "No input device found. " + PaEngine.LastError);
        _pair.ItemsSource = items;
        _pair.SelectedIndex = Math.Min(AppSettings.InputStart, Math.Max(0, items.Count - 1));
    }

    private void Save()
    {
        int i = _dev.SelectedIndex;
        AppSettings.AsioDriver = i > 0 && i - 1 < _devices.Count ? _devices[i - 1].Name : "";
        AppSettings.InputStart = Math.Max(0, _pair.SelectedIndex);
        AppSettings.SampleRate = _rate.SelectedIndex switch { 1 => 48000, 2 => 96000, 3 => 192000, _ => 0 };
        AppSettings.Save();
    }

    private void ToggleTest()
    {
        if (_cap != null) { StopTest(); return; }
        var d = Chosen();
        if (d == null) { _test.Text = Ui.Tt("Ni vhodne naprave.", "No input device."); return; }
        int ch = Math.Min(d.MaxIn, 32);
        lock (_lock) _peak = new float[ch];
        _cap = new PaCapture();
        string? err = _cap.Start(d, 0, ch, (buf, n, c) =>
        {
            lock (_lock)
            {
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < c && k < _peak.Length; k++)
                    {
                        float a = Math.Abs(buf[i * c + k]);
                        if (a > _peak[k]) _peak[k] = a;
                    }
            }
        });
        if (err != null) { _test.Text = Ui.Tt("Napaka: ", "Error: ") + err; _cap = null; return; }
        _timer.Start();
        _testBtn.Text = Ui.Tt("Ustavi preizkus", "Stop test");
    }

    private void StopTest()
    {
        _timer.Stop();
        _cap?.Stop(); _cap = null;
        _testBtn.Text = Ui.Tt("Zaženi preizkus", "Start test");
    }

    private void ShowLevels()
    {
        float[] p;
        lock (_lock) { p = (float[])_peak.Clone(); Array.Clear(_peak); }
        var sb = new StringBuilder();
        int best = 0;
        for (int i = 1; i < p.Length; i++) if (p[i] > p[best]) best = i;
        for (int i = 0; i < p.Length; i++)
        {
            double db = p[i] > 1e-6 ? 20 * Math.Log10(p[i]) : double.NegativeInfinity;
            int bars = double.IsNegativeInfinity(db) ? 0 : (int)Math.Clamp((db + 80) / 80 * 30, 0, 30);
            string dbs = double.IsNegativeInfinity(db) ? "  −inf" : db.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(6);
            sb.AppendLine($"{(i + 1),2}: {dbs} dBFS  {new string('█', bars)}{(i == best && p[best] > 1e-4 ? "  ◄" : "")}");
        }
        _test.Text = sb.ToString();
    }
}
