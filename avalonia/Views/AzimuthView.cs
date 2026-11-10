using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CDSMeasure.Audio;
using CDSMeasure.Controls;
using CDSMeasure.Core;

namespace CDSMeasure.Views;

/// <summary>Azimut glave (predvajanje): fazna razlika L − D testnega tona 10 / 12,5 / 15 kHz v živo.</summary>
public sealed class AzimuthView : UserControl, IDisposable
{
    private double _zoneUs = 2.0, _range = 90, _nominal, _maxSum = double.NegativeInfinity;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private PaCapture? _cap;
    private AzimuthCore? _core;
    private bool _running, _showLiss;
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private readonly CdsButton _start = new(true), _shot = new(), _resetMax = new();
    private readonly TextBlock _phase = new(), _delay = new(), _tone = new(), _levels = new(), _warn = new(), _status = new(), _hint = new(), _source = new();
    private readonly AzimuthGauge _gauge = new();
    private readonly PhaseTrace _trace = new();
    private readonly LissajousScope _liss = new();
    private readonly Grid _scopeHost = new();
    private readonly float[] _lsL = new float[1024], _lsR = new float[1024];
    private readonly List<(Seg Seg, string Key)> _labelSegs = new();
    private readonly List<TextBlock> _groupLabels = new();
    private readonly List<string> _groupKeys = new();

    public AzimuthView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 18, 4);
        _start.Click += () => { if (_running) StopMeasure(); else StartMeasure(); };
        bar.Children.Add(_start);
        AddGroup(bar, "az.freq", v => { _nominal = v; if (_core != null) _core.NominalHz = _nominal; },
            ("az.auto", 0.0), ("10 kHz", 10000.0), ("12,5 kHz", 12500.0), ("15 kHz", 15000.0));
        AddGroup(bar, "az.range", v => { _range = v; RefreshStatic(); },
            ("±30°", 30.0), ("±90°", 90.0), ("±180°", 180.0));
        AddGroup(bar, "az.zone", v => { _zoneUs = v; RefreshStatic(); },
            ("±2 µs", 2.0), ("±5 µs", 5.0), ("±10 µs", 10.0));
        AddGroup(bar, "az.view", v => { _showLiss = v > 0; _liss.IsVisible = _showLiss; if (!_showLiss) _liss.Clear(); },
            ("az.v.trace", 0.0), ("az.v.liss", 1.0));
        _resetMax.Margin = new Thickness(6, 0, 0, 4); _shot.Margin = new Thickness(6, 0, 0, 4);
        _resetMax.Click += () => { _maxSum = double.NegativeInfinity; };
        _shot.Click += ExportPng;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(_resetMax); actions.Children.Add(_shot);
        var top = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions); top.Children.Add(bar);
        Grid.SetRow(top, 0); root.Children.Add(top);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var read = new Grid { Margin = new Thickness(18, 14, 18, 6) };
        for (int i = 0; i < 3; i++) read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Big(_phase, 56, Ui.GoldLight); Big(_delay, 40, Ui.Text); Big(_tone, 28, Ui.Dim);
        _tone.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_phase, 0); Grid.SetColumn(_delay, 1); Grid.SetColumn(_tone, 2);
        read.Children.Add(_phase); read.Children.Add(_delay); read.Children.Add(_tone);
        Grid.SetRow(read, 0); body.Children.Add(read);

        Grid.SetRow(_gauge, 1); body.Children.Add(_gauge);
        _scopeHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _scopeHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _trace.Margin = new Thickness(0, 6, 0, 0);
        _liss.Margin = new Thickness(12, 6, 0, 0); _liss.IsVisible = false;
        _scopeHost.SizeChanged += (_, e) => _liss.Width = Math.Max(0, e.NewSize.Height - 6);
        Grid.SetColumn(_trace, 0); Grid.SetColumn(_liss, 1);
        _scopeHost.Children.Add(_trace); _scopeHost.Children.Add(_liss);
        Grid.SetRow(_scopeHost, 2); body.Children.Add(_scopeHost);

        var low = new StackPanel { Margin = new Thickness(18, 8, 18, 12) };
        _levels.FontFamily = Ui.Mono; _levels.FontSize = 16; _levels.Foreground = Ui.Text;
        _warn.FontSize = 13; _warn.Foreground = Ui.Red; _warn.Margin = new Thickness(0, 4, 0, 0);
        _hint.FontSize = 12; _hint.Foreground = Ui.Dim; _hint.TextWrapping = TextWrapping.Wrap; _hint.Margin = new Thickness(0, 6, 0, 0);
        low.Children.Add(_levels); low.Children.Add(_warn); low.Children.Add(_hint);
        Grid.SetRow(low, 3); body.Children.Add(low);

        var panel = Ui.PanelBox(body, new Thickness(0));
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _source.FontSize = 12; _source.Foreground = Ui.Dim;
        _status.FontSize = 12; _status.Foreground = Ui.Gold; _status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_status, 1);
        foot.Children.Add(_source); foot.Children.Add(_status);
        Grid.SetRow(foot, 2); root.Children.Add(foot);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        RefreshStatic();
        ShowEmpty();
    }

    private static void Big(TextBlock tb, double size, IBrush brush)
    {
        tb.FontFamily = Ui.Mono; tb.FontSize = size; tb.Foreground = brush; tb.VerticalAlignment = VerticalAlignment.Center;
    }

    private void AddGroup(WrapPanel host, string labelKey, Action<double> on, params (string Key, double Val)[] items)
    {
        var tb = Ui.Label(); tb.Margin = new Thickness(0, 0, 8, 4);
        _groupLabels.Add(tb); _groupKeys.Add(labelKey); host.Children.Add(tb);
        var group = new List<Seg>();
        bool first = true;
        foreach (var (key, val) in items)
        {
            var seg = new Seg(key) { On = key == "±90°" || (labelKey != "az.range" && first) };
            double v = val;
            seg.Click += () => { foreach (var s in group) s.On = false; seg.On = true; on(v); };
            group.Add(seg); _labelSegs.Add((seg, key)); host.Children.Add(seg);
            if (seg.On) on(v);
            first = false;
        }
        host.Children.Add(new Border { Width = 18, Height = 1 });
    }

    private void ApplyLang()
    {
        _start.Text = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _shot.Text = Loc.T("wfs.shot");
        _resetMax.Text = Loc.T("az.resetmax");
        for (int i = 0; i < _groupLabels.Count; i++) _groupLabels[i].Text = Loc.T(_groupKeys[i]);
        foreach (var (seg, key) in _labelSegs) seg.Text = key.StartsWith("az.") ? Loc.T(key) : key;
        _hint.Text = Loc.T("az.hint");
        if (!_running) { _status.Text = Loc.T("az.idle"); _source.Text = Ui.Tt("Vhod: ", "Input: ") + PaEngine.CardText(); }
    }

    private double ZoneDeg(double freqHz) => _zoneUs * 1e-6 * 360.0 * (freqHz > 0 ? freqHz : (_nominal > 0 ? _nominal : 10000));

    private void RefreshStatic()
    {
        double f = _core?.Latest.FreqHz ?? 0;
        _gauge.Set(0, false, _range, ZoneDeg(f));
        _trace.Set(_range, ZoneDeg(f), _sw.Elapsed.TotalSeconds);
    }

    private void ShowEmpty() { _phase.Text = "—"; _delay.Text = ""; _tone.Text = ""; _levels.Text = ""; _warn.Text = ""; }

    private void StartMeasure()
    {
        var dev = PaEngine.Resolve();
        if (dev == null)
        {
            _status.Text = Ui.Tt("Ni vhodne zvočne naprave. Priključi kartico in odpri Nastavitve.", "No audio input device. Connect a card and open Settings.")
                           + (PaEngine.LastError.Length > 0 ? "  (" + PaEngine.LastError + ")" : "");
            return;
        }
        _cap ??= new PaCapture();
        _core = null;
        string? err = _cap.Start(dev, AppSettings.InputStart, 2, (buf, n, c) => _core?.Process(buf, n, c));
        if (err != null) { _core = null; _status.Text = Ui.Tt("Napaka zvočne kartice: ", "Audio device error: ") + err; return; }
        if (_cap.Channels < 2)
        {
            _cap.Stop(); _core = null;
            _status.Text = Loc.T("az.nostereo");
            return;
        }
        _core = new AzimuthCore(_cap.SampleRate, _cap.Channels) { NominalHz = _nominal };
        _maxSum = double.NegativeInfinity;
        _trace.Clear();
        _sw.Restart(); _timer.Start(); _running = true;
        _source.Text = Ui.Tt("Vhod: ", "Input: ") + $"{_cap.DeviceName} · {PaEngine.ApiName} · {_cap.SampleRate / 1000.0:0.#} kHz";
        _status.Text = Loc.T("az.waiting");
        ApplyLang();
    }

    private void StopMeasure()
    {
        _cap?.Stop();
        _timer.Stop(); _sw.Stop();
        _running = false; _core = null;
        _gauge.Set(0, false, _range, ZoneDeg(0));
        _liss.Clear();
        ShowEmpty();
        ApplyLang();
    }

    private void Tick()
    {
        var core = _core;
        if (core == null) return;
        var r = core.Latest;
        double t = _sw.Elapsed.TotalSeconds;
        double zone = ZoneDeg(r.Valid ? r.FreqHz : 0);
        _gauge.Set(r.PhaseDeg, r.Valid, _range, zone);
        if (r.Valid) _trace.Add(t, r.PhaseDeg);
        _trace.Set(_range, zone, t);
        if (_showLiss)
        {
            int n = core.Snapshot(_lsL, _lsR);
            _liss.Set(_lsL, _lsR, n, zone);
        }
        if (!r.Valid)
        {
            _phase.Text = "—"; _delay.Text = ""; _tone.Text = ""; _levels.Text = "";
            _warn.Text = (r.PeakLDb > -0.5 || r.PeakRDb > -0.5) ? Loc.T("az.clip") : "";
            _status.Text = Loc.T("az.waiting");
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        _phase.Text = r.PhaseDeg.ToString("+0.0;-0.0;0.0", inv) + " °";
        _delay.Text = r.DelayUs.ToString("+0.00;-0.00;0.00", inv) + " µs";
        _tone.Text = (r.FreqHz / 1000.0).ToString("0.00", inv) + " kHz";
        if (r.SumDb > _maxSum) _maxSum = r.SumDb;
        _levels.Text = $"L {r.LevelLDb.ToString("0.0", inv)}   R {r.LevelRDb.ToString("0.0", inv)}   L+R {r.SumDb.ToString("0.0", inv)}   dBFS   (max L+R {_maxSum.ToString("0.0", inv)})";
        _warn.Text = (r.PeakLDb > -0.5 || r.PeakRDb > -0.5) ? Loc.T("az.clip")
                   : (Math.Abs(r.LevelLDb - r.LevelRDb) > 3 ? Loc.T("az.imbalance") : "");
        _status.Text = Loc.T("az.measuring");
    }

    private void ExportPng()
    {
        try
        {
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            if (size.Width < 10 || size.Height < 10) return;
            using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(this);
            string dir = AppSettings.OutDir();
            string file = System.IO.Path.Combine(dir, $"AZ Screen {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            rtb.Save(file);
            _status.Text = Loc.T("wfs.saved") + file;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    public void Dispose()
    {
        Loc.Changed -= ApplyLang;
        _timer.Stop();
        try { _cap?.Stop(); } catch { }
        _core = null;
    }
}
