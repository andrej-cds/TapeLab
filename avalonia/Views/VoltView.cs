using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CDSMeasure.Audio;
using CDSMeasure.Controls;
using CDSMeasure.Core;

namespace CDSMeasure.Views;

/// <summary>Voltmeter (AC True RMS): dva traka čez celo širino (L modra, D rdeča), območja po kanalu, Lock, ena vrsta tipk za območja.</summary>
public sealed class VoltView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private PaCapture? _cap;
    private VoltCore? _core;
    private bool _running;

    private readonly int[] _rangeIdx = { VoltRanges.DefaultIndex, VoltRanges.DefaultIndex };
    private int _active;            // kanal, na katerega delujejo tipke območij (0 = L, 1 = D)
    private bool _lock = true;
    private readonly double[] _shown = new double[2];   // glajena prikazana napetost (V)

    private readonly CdsButton _start = new(true), _lockBtn = new(), _calL = new(), _calR = new(), _calReset = new();
    private readonly Seg _selL = new("L"), _selR = new("R");
    private readonly TextBox _refBox = new();
    private readonly TextBlock _chLabel = Ui.Label(), _rangeLabel = Ui.Label(), _refLabel = Ui.Label(), _warn = new(), _status = new(), _hint = new();
    private readonly VoltStrip _stripL = new(), _stripR = new();
    private readonly List<Seg> _rangeBtns = new();

    public VoltView()
    {
        VoltCal.Load();

        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- zgornja vrstica: Start, kanal, Lock, območja | umerjanje ----
        var bar = new WrapPanel();
        _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 18, 4);
        _start.Click += () => { if (_running) StopMeasure(); else StartMeasure(); };
        bar.Children.Add(_start);

        _chLabel.Margin = new Thickness(0, 0, 8, 4);
        bar.Children.Add(_chLabel);
        _selL.On = true;
        _selL.Click += () => { _selL.On = true; _selR.On = false; SelectChannel(0); };
        _selR.Click += () => { _selR.On = true; _selL.On = false; SelectChannel(1); };
        bar.Children.Add(_selL); bar.Children.Add(_selR);
        bar.Children.Add(new Border { Width = 18, Height = 1 });

        _lockBtn.Margin = new Thickness(0, 0, 18, 4); _lockBtn.MinWidth = 120;
        _lockBtn.Click += () => SetLock(!_lock);
        bar.Children.Add(_lockBtn);

        _rangeLabel.Margin = new Thickness(0, 0, 10, 4);
        bar.Children.Add(_rangeLabel);
        for (int i = 0; i < VoltRanges.All.Length; i++)
        {
            int idx = i;
            var rb = new Seg(VoltRanges.All[i].Name);
            rb.Click += () => SetRange(idx);
            _rangeBtns.Add(rb); bar.Children.Add(rb);
        }

        var cal = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        _refLabel.Margin = new Thickness(0, 0, 8, 4);
        cal.Children.Add(_refLabel);
        _refBox.Width = 90; _refBox.Text = "90"; _refBox.Margin = new Thickness(0, 0, 8, 4);
        _refBox.FontFamily = Ui.Mono; _refBox.FontSize = 14;
        cal.Children.Add(_refBox);
        _calL.Margin = new Thickness(0, 0, 8, 4); _calR.Margin = new Thickness(0, 0, 8, 4); _calReset.Margin = new Thickness(0, 0, 0, 4);
        _calL.Click += () => Calibrate(0);
        _calR.Click += () => Calibrate(1);
        _calReset.Click += () => { VoltCal.Reset(); _status.Text = Loc.T("volt.calreset"); UpdateWarn(); };
        cal.Children.Add(_calL); cal.Children.Add(_calR); cal.Children.Add(_calReset);

        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(bar, 0); Grid.SetColumn(cal, 1);
        top.Children.Add(bar); top.Children.Add(cal);
        Grid.SetRow(top, 0); root.Children.Add(top);

        // ---- telo: dva traka ----
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 110 });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 110 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _stripL.NeedleBrush = Ui.Blue; _stripR.NeedleBrush = Ui.Red;
        _stripL.Margin = new Thickness(0, 0, 0, 8);
        _stripL.PointerPressed += (_, _) => { _selL.On = true; _selR.On = false; SelectChannel(0); };
        _stripR.PointerPressed += (_, _) => { _selR.On = true; _selL.On = false; SelectChannel(1); };
        Grid.SetRow(_stripL, 0); body.Children.Add(_stripL);
        Grid.SetRow(_stripR, 1); body.Children.Add(_stripR);

        var low = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _warn.FontSize = 13; _warn.Foreground = Ui.Red; _warn.TextWrapping = TextWrapping.Wrap;
        _hint.FontSize = 12; _hint.Foreground = Ui.Dim; _hint.TextWrapping = TextWrapping.Wrap; _hint.Margin = new Thickness(0, 4, 0, 0);
        low.Children.Add(_warn); low.Children.Add(_hint);
        Grid.SetRow(low, 2); body.Children.Add(low);

        var panel = Ui.PanelBox(body, new Thickness(18, 14, 18, 12));
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        _status.FontSize = 12; _status.Foreground = Ui.Gold; _status.Margin = new Thickness(0, 8, 0, 0);
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
        _start.Text = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _chLabel.Text = Loc.T("volt.channel");
        _lockBtn.Text = Loc.T(_lock ? "volt.lockon" : "volt.lockoff");
        _rangeLabel.Text = Loc.T("volt.range");
        _stripL.Caption = Loc.T("volt.left"); _stripR.Caption = Loc.T("volt.right");
        _refLabel.Text = Loc.T("volt.ref");
        _calL.Text = Loc.T("volt.calL"); _calR.Text = Loc.T("volt.calR"); _calReset.Text = Loc.T("volt.calres");
        _hint.Text = Loc.T("volt.hint");
        if (!_running) _status.Text = Loc.T("volt.idle");
        UpdateWarn();
    }

    // ---------- območja ----------
    private void SelectChannel(int ch) { _active = ch; ApplyRanges(); }

    private void SetLock(bool on)
    {
        _lock = on;
        if (on) _rangeIdx[1 - _active] = _rangeIdx[_active];
        _lockBtn.Text = Loc.T(_lock ? "volt.lockon" : "volt.lockoff");
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
        _stripL.Selected = _lock || _active == 0;
        _stripR.Selected = _lock || _active == 1;
        for (int i = 0; i < _rangeBtns.Count; i++) _rangeBtns[i].On = i == _rangeIdx[_active];
        UpdateReadouts();
    }

    // ---------- zajem ----------
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
        string? err = _cap.Start(dev, AppSettings.InputStart, 2, (buf, n, c) =>
        {
            var core = _core;
            core?.Process(buf, n, c);
        });
        if (err != null) { _core = null; _status.Text = Ui.Tt("Napaka zvočne kartice: ", "Audio device error: ") + err; return; }
        _core = new VoltCore(_cap.SampleRate, _cap.Channels);
        _shown[0] = _shown[1] = 0;
        _timer.Start(); _running = true;
        _status.Text = Loc.T("volt.measuring") + $"   {PaEngine.ApiName} · {_cap.DeviceName} · {_cap.SampleRate / 1000.0:0.#} kHz";
        ApplyLang();
    }

    private void StopMeasure()
    {
        _cap?.Stop();
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
            double a = v > _shown[ch] ? 0.45 : 0.18;      // hiter dvig, počasnejši padec
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
        string t = (_refBox.Text ?? "").Trim().Replace(',', '.');
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
        try { _cap?.Stop(); } catch { }
        _core = null;
    }
}
