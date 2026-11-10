using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CDSMeasure.Controls;
using CDSMeasure.Core;

namespace CDSMeasure.Views;

/// <summary>Voltmeter (AC True RMS): dva traka čez celo širino (L modra, D rdeča), območja po kanalu, Lock, ena vrsta tipk za območja.</summary>
public sealed class VoltView : UserControl, IDisposable
{
    private static readonly Brush BlueBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x9C, 0xFF));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0x00, 0x1B));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private AsioCapture? _asio;
    private VoltCore? _core;
    private bool _running;

    private readonly int[] _rangeIdx = { VoltRanges.DefaultIndex, VoltRanges.DefaultIndex };
    private int _active;            // kanal, na katerega delujejo tipke območij (0 = L, 1 = D)
    private bool _lock = true;
    private bool _syncing;
    private readonly double[] _shown = new double[2];   // glajena prikazana napetost (V)

    private readonly Button _start = new(), _lockBtn = new(), _calL = new(), _calR = new(), _calReset = new();
    private readonly RadioButton _selL = new(), _selR = new();
    private readonly TextBox _refBox = new();
    private readonly TextBlock _chLabel = new(), _rangeLabel = new(), _refLabel = new(), _warn = new(), _status = new(), _hint = new();
    private readonly VoltStrip _stripL = new(), _stripR = new();
    private readonly List<RadioButton> _rangeBtns = new();

    private static object Res(string key) => Application.Current.Resources[key];

    public VoltView()
    {
        VoltCal.Load();

        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- zgornja vrstica: Start, kanal, Lock ----
        var bar = new WrapPanel();
        _start.Style = (Style)Res("CdsPrimary"); _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 18, 4);
        _start.Click += (_, _) => { if (_running) StopMeasure(); else StartMeasure(); };
        bar.Children.Add(_start);

        _chLabel.Style = (Style)Res("DimLabel"); _chLabel.Margin = new Thickness(0, 0, 8, 4); _chLabel.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(_chLabel);
        _selL.Style = (Style)Res("CdsSeg"); _selL.GroupName = "volt.ch"; _selL.Content = "L"; _selL.IsChecked = true; _selL.Margin = new Thickness(0, 0, 0, 4);
        _selR.Style = (Style)Res("CdsSeg"); _selR.GroupName = "volt.ch"; _selR.Content = "R"; _selR.Margin = new Thickness(0, 0, 0, 4);
        _selL.Checked += (_, _) => SelectChannel(0);
        _selR.Checked += (_, _) => SelectChannel(1);
        bar.Children.Add(_selL); bar.Children.Add(_selR);
        bar.Children.Add(new Border { Width = 18 });

        _lockBtn.Style = (Style)Res("CdsButton"); _lockBtn.Margin = new Thickness(0, 0, 18, 4); _lockBtn.MinWidth = 120;
        _lockBtn.Click += (_, _) => SetLock(!_lock);
        bar.Children.Add(_lockBtn);

        // tipke območij: ena vrsta za oba kanala (v zgornji vrstici)
        _rangeLabel.Style = (Style)Res("DimLabel"); _rangeLabel.Margin = new Thickness(0, 0, 10, 4); _rangeLabel.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(_rangeLabel);
        for (int i = 0; i < VoltRanges.All.Length; i++)
        {
            int idx = i;
            var rb = new RadioButton { Style = (Style)Res("CdsSeg"), GroupName = "volt.range", Content = VoltRanges.All[i].Name, Margin = new Thickness(0, 0, 0, 4) };
            rb.Checked += (_, _) => { if (!_syncing) SetRange(idx); };
            _rangeBtns.Add(rb); bar.Children.Add(rb);
        }

        // umerjanje: na desni strani zgornje vrstice
        var cal = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        _refLabel.Style = (Style)Res("DimLabel"); _refLabel.Margin = new Thickness(0, 0, 8, 4); _refLabel.VerticalAlignment = VerticalAlignment.Center;
        cal.Children.Add(_refLabel);
        _refBox.Width = 80; _refBox.Text = "90"; _refBox.Margin = new Thickness(0, 0, 8, 4); _refBox.VerticalContentAlignment = VerticalAlignment.Center;
        _refBox.FontFamily = (FontFamily)Res("FontMono"); _refBox.FontSize = 14;
        cal.Children.Add(_refBox);
        _calL.Style = (Style)Res("CdsButton"); _calL.Margin = new Thickness(0, 0, 8, 4);
        _calR.Style = (Style)Res("CdsButton"); _calR.Margin = new Thickness(0, 0, 8, 4);
        _calReset.Style = (Style)Res("CdsButton"); _calReset.Margin = new Thickness(0, 0, 0, 4);
        _calL.Click += (_, _) => Calibrate(0);
        _calR.Click += (_, _) => Calibrate(1);
        _calReset.Click += (_, _) => { VoltCal.Reset(); _status.Text = Loc.T("volt.calreset"); UpdateWarn(); };
        cal.Children.Add(_calL); cal.Children.Add(_calR); cal.Children.Add(_calReset);
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(bar, 0); Grid.SetColumn(cal, 1);
        top.Children.Add(bar); top.Children.Add(cal);
        Grid.SetRow(top, 0); root.Children.Add(top);

        // ---- telo: odčitka, trakova, tipke območij ----
        var body = new Grid { Margin = new Thickness(18, 14, 18, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 110 }); // trak L
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 110 }); // trak R
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                               // opozorila

        _stripL.NeedleBrush = BlueBrush;
        _stripR.NeedleBrush = RedBrush;
        _stripL.Margin = new Thickness(0, 0, 0, 8);
        _stripL.MouseLeftButtonDown += (_, _) => { _selL.IsChecked = true; };
        _stripR.MouseLeftButtonDown += (_, _) => { _selR.IsChecked = true; };
        Grid.SetRow(_stripL, 0); body.Children.Add(_stripL);
        Grid.SetRow(_stripR, 1); body.Children.Add(_stripR);

        var low = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _warn.FontSize = 13; _warn.Foreground = (Brush)Res("RedBrush"); _warn.TextWrapping = TextWrapping.Wrap;
        _hint.FontSize = 12; _hint.Foreground = (Brush)Res("DimBrush"); _hint.TextWrapping = TextWrapping.Wrap; _hint.Margin = new Thickness(0, 4, 0, 0);
        low.Children.Add(_warn); low.Children.Add(_hint);
        Grid.SetRow(low, 2); body.Children.Add(low);

        var panel = new Border { Style = (Style)Res("CdsPanel"), Child = body, Margin = new Thickness(0) };
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        _status.FontSize = 12; _status.Foreground = (Brush)Res("GoldBrush"); _status.Margin = new Thickness(0, 8, 0, 0);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(_status, 2); root.Children.Add(_status);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        ApplyRanges();
        ShowEmpty();
    }

    private void ApplyLang()
    {
        _start.Content = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _chLabel.Text = Loc.T("volt.channel");
        _lockBtn.Content = Loc.T(_lock ? "volt.lockon" : "volt.lockoff");
        _rangeLabel.Text = Loc.T("volt.range");
        _stripL.Caption = Loc.T("volt.left"); _stripR.Caption = Loc.T("volt.right");
        _refLabel.Text = Loc.T("volt.ref");
        _calL.Content = Loc.T("volt.calL"); _calR.Content = Loc.T("volt.calR"); _calReset.Content = Loc.T("volt.calres");
        _hint.Text = Loc.T("volt.hint");
        if (!_running) _status.Text = Loc.T("volt.idle");
        UpdateWarn();
    }

    // ---------- območja ----------
    private void SelectChannel(int ch)
    {
        _active = ch;
        ApplyRanges();
    }

    private void SetLock(bool on)
    {
        _lock = on;
        if (on) { _rangeIdx[1 - _active] = _rangeIdx[_active]; }
        _lockBtn.Content = Loc.T(_lock ? "volt.lockon" : "volt.lockoff");
        ApplyRanges();
    }

    private void SetRange(int idx)
    {
        _rangeIdx[_active] = idx;
        if (_lock) _rangeIdx[1 - _active] = idx;
        ApplyRanges();
    }

    private void ApplyRanges()
    {
        _stripL.Range = VoltRanges.All[_rangeIdx[0]];
        _stripR.Range = VoltRanges.All[_rangeIdx[1]];
        _stripL.Selected = !_lock && _active == 0 || _lock;
        _stripR.Selected = !_lock && _active == 1 || _lock;
        _syncing = true;
        for (int i = 0; i < _rangeBtns.Count; i++) _rangeBtns[i].IsChecked = i == _rangeIdx[_active];
        _syncing = false;
        UpdateReadouts();
    }

    // ---------- zajem ----------
    private Action<float[], int, int> MakeSink(int rate, int ch)
    {
        var core = new VoltCore(rate, ch);
        _core = core;
        return (buf, n, c) => core.Process(buf, n, c);
    }

    private void StartMeasure()
    {
        _asio ??= new AsioCapture();
        string? drv = AsioCapture.FindDefault();
        if (drv == null) { _status.Text = Loc.T("wfs.noasio"); return; }
        _core = null;
        string? err = _asio.StartRaw(drv, MakeSink);
        if (err != null) { _core = null; _status.Text = Loc.T("wfs.asioerr") + err; return; }
        _shown[0] = _shown[1] = 0;
        _timer.Start(); _running = true;
        _status.Text = Loc.T("volt.measuring") + $"   ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
        ApplyLang();
    }

    private void StopMeasure()
    {
        _asio?.Stop();
        _timer.Stop();
        _running = false;
        _core = null;
        ShowEmpty();
        ApplyLang();
    }

    private void ShowEmpty()
    {
        _shown[0] = _shown[1] = 0;
        _stripL.Value = 0; _stripR.Value = 0;
    }

    private double TrueVolts(int ch)
    {
        var core = _core;
        if (core == null || !core.Valid || ch >= core.Channels) return 0;
        return VoltCal.ToVolts(ch, core.Rms(ch));
    }

    private void Tick()
    {
        var core = _core;
        if (core == null || !core.Valid) return;
        for (int ch = 0; ch < 2; ch++)
        {
            double v = TrueVolts(ch);
            // blaga balistika kazalca (hiter dvig, počasnejši padec)
            double a = v > _shown[ch] ? 0.45 : 0.18;
            _shown[ch] += a * (v - _shown[ch]);
        }
        UpdateReadouts();
        UpdateWarn();
    }

    private void UpdateReadouts()
    {
        if (!_running) return;
        _stripL.Value = _shown[0]; _stripR.Value = _shown[1];
    }

    private void UpdateWarn()
    {
        var parts = new List<string>();
        if (!VoltCal.IsCalibrated(0) || !VoltCal.IsCalibrated(1)) parts.Add(Loc.T("volt.uncal"));
        var core = _core;
        if (_running && core != null && core.Valid && (core.Peak(0) > 0.98 || (core.Channels > 1 && core.Peak(1) > 0.98))) parts.Add(Loc.T("az.clip"));
        if (_running && core != null && core.Channels < 2) parts.Add(Loc.T("volt.mono"));
        _warn.Text = string.Join("   ", parts);
    }

    // ---------- umerjanje ----------
    private void Calibrate(int ch)
    {
        var core = _core;
        if (!_running || core == null || !core.Valid || ch >= core.Channels) { _status.Text = Loc.T("volt.calneed"); return; }
        string t = _refBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double mv) || mv <= 0)
        { _status.Text = Loc.T("volt.calbad"); return; }
        if (VoltCal.Set(ch, core.Rms(ch), mv / 1000.0)) _status.Text = Loc.T("volt.caldone") + (ch == 0 ? "L" : "R") + $" = {mv.ToString("0.###", CultureInfo.InvariantCulture)} mV";
        else _status.Text = Loc.T("volt.calnosig");
        UpdateWarn();
    }

    public void Dispose()
    {
        Loc.Changed -= ApplyLang;
        _timer.Stop();
        try { _asio?.Stop(); } catch { }
        _core = null;
    }
}
