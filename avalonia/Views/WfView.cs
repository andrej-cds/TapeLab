using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CDSMeasure.Audio;
using CDSMeasure.Controls;
using CDSMeasure.Core;
using WPoint = System.Windows.Point;

namespace CDSMeasure.Views;

/// <summary>Wow &amp; Flutter in hitrost: odstopanje hitrosti, časovni graf, W&amp;F (JIS / DIN / neutežen), spekter.</summary>
public sealed class WfView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly System.Diagnostics.Stopwatch _sw = new();
    private PaCapture? _cap;
    private WfCore? _core;
    private volatile int _chan;
    private bool _running;
    private double _refHz = 3000, _wfRange = 0.15, _window = 10, _specWin = 16, _durationMin = 45;
    private int _mode, _ticks;
    private double _speedShown, _wfShown, _noSigT = -1;
    private const int MinValidSec = 30;
    private const double NoSignalStopSec = 10;
    private readonly List<WPoint> _speed = new(), _wf = new(), _pk = new(), _hz = new();
    private readonly List<float> _raw = new();
    private double[]? _specAvg; private int _specKey;
    private MechProfile _mech = MechProfile.Generic;
    private ModelSpec? _model, _defModel;
    private bool _over;
    private readonly WrapPanel _mechPanel = new(), _modelPanel = new();
    private readonly List<(Seg Seg, MechProfile Prof)> _mechSegs = new();

    private readonly CdsButton _start = new(true), _shot = new(), _csv = new();
    private readonly TextBlock _elapsed = new(), _level = new(), _status = new(), _source = new(), _endText = new(), _period = new();
    private readonly TextBlock _speedHz = new(), _speedPct = new(), _wfValue = new(), _wfModeText = new();
    private readonly StripMeter _speedMeter = new(), _wfMeter = new();
    private readonly LineChart _timeChart = new(), _specChart = new();
    private readonly List<(TextBlock Tb, string Key)> _texts = new();
    private readonly List<(Seg Seg, Func<string> Text)> _segs = new();

    public WfView()
    {
        var root = new Grid { Margin = new Thickness(16, 12, 16, 8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- ukazna vrstica ----
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        _start.MinWidth = 100; _start.Margin = new Thickness(0, 0, 14, 4);
        _start.Click += () => { if (_running) Stop(); else Begin(); };
        bar.Children.Add(_start);
        var tm = new StackPanel { Margin = new Thickness(0, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center };
        _elapsed.Text = "00:00:00"; _elapsed.FontFamily = Ui.Mono; _elapsed.FontSize = 18; _elapsed.Foreground = Ui.Text;
        _level.FontFamily = Ui.Mono; _level.FontSize = 10; _level.Foreground = Ui.Gold;
        tm.Children.Add(_elapsed); tm.Children.Add(_level);
        bar.Children.Add(tm);
        Group(bar, "wfs.duration", null, d => _durationMin = d, 45, ("15", 15), ("30", 30), ("45", 45), ("60", 60));
        Group(bar, "wfs.chan", null, c => { _chan = (int)c; }, 0, ("L", 0), (null!, 1), (null!, 2));
        Group(bar, "wfs.ref", null, r => { _refHz = r; if (_running) { Stop(); Begin(); } }, 3000, ("3000 Hz", 3000), ("3150 Hz", 3150));
        _shot.Margin = new Thickness(6, 0, 0, 4); _csv.Margin = new Thickness(6, 0, 0, 4);
        _shot.Click += ExportPng; _csv.Click += ExportCsv;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(_shot); actions.Children.Add(_csv);
        var top = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions); top.Children.Add(bar);
        var barHost = new StackPanel();
        barHost.Children.Add(top);
        BuildMech();
        barHost.Children.Add(_mechPanel); barHost.Children.Add(_modelPanel);
        _endText.HorizontalAlignment = HorizontalAlignment.Center; _endText.FontFamily = Ui.Head; _endText.FontSize = 20; _endText.Foreground = Ui.Gold;
        barHost.Children.Add(_endText);
        Grid.SetRow(barHost, 0); root.Children.Add(barHost);

        // ---- 1. hitrost ----
        _speedMeter.Minimum = -3; _speedMeter.Maximum = 3; _speedMeter.MajorStep = 1; _speedMeter.MinorPerMajor = 10; _speedMeter.LabelFormat = "+0;-0;0"; _speedMeter.Caption = "%";
        Style(_speedHz, _speedPct, "----  Hz", "----  %");
        var p1 = MeterPanel("wfs.speed", _speedMeter, _speedHz, _speedPct, null);
        Grid.SetRow(p1, 1); root.Children.Add(p1);

        // ---- 2. čas ----
        _timeChart.YFormat = "+0.00;-0.00;0.00"; _timeChart.XFormat = "0"; _timeChart.XUnit = "s"; _timeChart.YUnit = "%";
        var winRow = new StackPanel { Orientation = Orientation.Horizontal };
        winRow.Children.Add(_period); _period.Margin = new Thickness(0, 0, 24, 0); _period.FontFamily = Ui.Mono; _period.FontSize = 13; _period.Foreground = Ui.Gold; _period.VerticalAlignment = VerticalAlignment.Center;
        AddLabel(winRow, "wfs.window");
        SegRow(winRow, w => { _window = w; Redraw(_sw.Elapsed.TotalSeconds); }, 10, ("1 s", 1), ("10 s", 10), ("60 s", 60));
        var p2 = ChartPanel("wfs.time", _timeChart, winRow);
        Grid.SetRow(p2, 2); root.Children.Add(p2);

        // ---- 3. W&F ----
        _wfMeter.Minimum = 0; _wfMeter.Maximum = 0.15; _wfMeter.MajorStep = 0.03; _wfMeter.MinorPerMajor = 4; _wfMeter.LabelFormat = "0.00"; _wfMeter.Caption = "%";
        Style(_wfValue, _wfModeText, "-.---  %", "");
        var ctl = new StackPanel { Orientation = Orientation.Horizontal };
        AddLabel(ctl, "wfs.mode");
        SegRow(ctl, m => { _mode = (int)m; ApplyLang(); UpdateLimit(); }, 0, ("wfs.wrms", 0), ("wfs.wpk", 1), ("wfs.unw", 2));
        AddLabel(ctl, "wfs.range");
        SegRow(ctl, r => { _wfRange = r; ApplyRange(); Redraw(_sw.Elapsed.TotalSeconds); }, 0.15, ("0.05 %", 0.05), ("0.15 %", 0.15), ("0.5 %", 0.5), ("1.0 %", 1));
        var p3 = MeterPanel("wfs.wf", _wfMeter, _wfValue, _wfModeText, ctl);
        Grid.SetRow(p3, 3); root.Children.Add(p3);

        // ---- 4. spekter ----
        _specChart.LogX = true; _specChart.XFormat = "0.##"; _specChart.YFormat = "0.000"; _specChart.XUnit = "Hz"; _specChart.YUnit = "%";
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        AddLabel(sw, "wfs.specwin");
        SegRow(sw, s => { _specWin = s; Redraw(_sw.Elapsed.TotalSeconds); }, 16, ("4 s", 4), ("8 s", 8), ("16 s", 16));
        var p4 = ChartPanel("wfs.spec", _specChart, sw);
        Grid.SetRow(p4, 4); root.Children.Add(p4);

        // ---- noga ----
        var foot = new Grid { Margin = new Thickness(2, 4, 2, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _source.FontSize = 12; _source.Foreground = Ui.Dim;
        _status.FontSize = 12; _status.Foreground = Ui.Gold;
        Grid.SetColumn(_status, 1);
        foot.Children.Add(_source); foot.Children.Add(_status);
        Grid.SetRow(foot, 5); root.Children.Add(foot);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        ApplyRange();
        Redraw(0);
    }

    // ---------- gradniki ----------
    private static void Style(TextBlock big, TextBlock small, string bigText, string smallText)
    {
        big.Text = bigText; big.FontFamily = Ui.Mono; big.FontSize = 26; big.Foreground = Ui.GoldLight; big.HorizontalAlignment = HorizontalAlignment.Right;
        small.Text = smallText; small.FontFamily = Ui.Mono; small.FontSize = 13; small.TextTrimming = TextTrimming.CharacterEllipsis; small.Foreground = Ui.Dim; small.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private void AddLabel(Panel host, string key)
    {
        var tb = Ui.Label(); tb.Margin = new Thickness(10, 0, 8, 0); tb.FontSize = 12;
        _texts.Add((tb, key)); host.Children.Add(tb);
    }

    private void SegRow(Panel host, Action<double> on, double def, params (string Key, double Val)[] items)
    {
        var group = new List<Seg>();
        foreach (var (key, val) in items)
        {
            var seg = new Seg(key) { On = val == def };
            seg.Margin = new Thickness(0, 0, 4, 0);
            double v = val; string k = key;
            seg.Click += () => { foreach (var s in group) s.On = false; seg.On = true; on(v); };
            group.Add(seg); host.Children.Add(seg);
            _segs.Add((seg, () => k.StartsWith("wfs.") ? Loc.T(k) : k));
        }
    }

    private void Group(WrapPanel host, string labelKey, string? _, Action<double> on, double def, params (string? Key, double Val)[] items)
    {
        var tb = Ui.Label(); tb.Margin = new Thickness(0, 0, 8, 4); tb.FontSize = 12;
        _texts.Add((tb, labelKey)); host.Children.Add(tb);
        var group = new List<Seg>();
        foreach (var (key, val) in items)
        {
            var seg = new Seg(key ?? "") { On = val == def };
            double v = val; double vv = val; string? k = key;
            seg.Click += () => { foreach (var s in group) s.On = false; seg.On = true; on(v); };
            group.Add(seg); host.Children.Add(seg);
            if (labelKey == "wfs.chan")
                _segs.Add((seg, () => vv == 0 ? "L" : vv == 1 ? (Loc.Lang == "sl" ? "D" : "R") : (Loc.Lang == "sl" ? "L+D" : "L+R")));
            else { string kk = k ?? ""; _segs.Add((seg, () => kk)); }
        }
        host.Children.Add(new Border { Width = 14, Height = 1 });
    }

    private Border MeterPanel(string titleKey, StripMeter meter, TextBlock big, TextBlock small, Control? controls)
    {
        var title = new TextBlock { FontSize = 13, Foreground = Ui.Gold, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _texts.Add((title, titleKey));
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (controls != null) { DockPanel.SetDock(controls, Dock.Right); head.Children.Add(controls); }
        head.Children.Add(title);
        var row = new Grid { Height = 80 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var digital = new Border { Background = Ui.Bg, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 4) };
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        st.Children.Add(big); st.Children.Add(small);
        digital.Child = st;
        Grid.SetColumn(digital, 1);
        row.Children.Add(meter); row.Children.Add(digital);
        var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(row);
        var b = Ui.PanelBox(sp, new Thickness(12, 8)); b.Margin = new Thickness(0, 0, 0, 6);
        return b;
    }

    private Border ChartPanel(string titleKey, LineChart chart, Control controls)
    {
        var title = new TextBlock { FontSize = 13, Foreground = Ui.Gold, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _texts.Add((title, titleKey));
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(controls, Dock.Right); head.Children.Add(controls); head.Children.Add(title);
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(head); Grid.SetRow(chart, 1); g.Children.Add(chart);
        var b = Ui.PanelBox(g, new Thickness(12, 8)); b.Margin = new Thickness(0, 0, 0, 6); b.MinHeight = 110;
        return b;
    }

    private void ApplyLang()
    {
        _start.Text = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _shot.Text = Loc.T("wfs.shot"); _csv.Text = Loc.T("wfs.csv");
        foreach (var (tb, key) in _texts) tb.Text = Loc.T(key) + (key == "wfs.duration" ? " (min)" : "");
        foreach (var (seg, text) in _segs) seg.Text = text();
        _wfModeText.Text = _mode switch { 0 => Loc.T("wfs.wrms"), 1 => Loc.T("wfs.wpk"), _ => Loc.T("wfs.unw") };
        if (!_running) _source.Text = Ui.Tt("Vhod: ", "Input: ") + PaEngine.CardText();
    }

    // ---------- mehanizem (neobvezno) ----------
    private void BuildMech()
    {
        _mechPanel.Margin = new Thickness(0, 0, 0, 2);
        _modelPanel.Margin = new Thickness(0, 0, 0, 4);
        var lab = Ui.Label(); lab.Margin = new Thickness(0, 6, 10, 4); lab.FontSize = 12; lab.VerticalAlignment = VerticalAlignment.Center;
        _texts.Add((lab, "wfs.mech")); _mechPanel.Children.Add(lab);
        foreach (var id in new[] { "sankyo", "sankyobd", "sankyosd", "classic", "zx9", "dragon", "none" })
        {
            var prof = MechProfile.All.First(m => m.Id == id);
            var seg = new Seg("") { On = id == "none", Margin = new Thickness(0, 0, 4, 4) };
            if (prof.Models.Length > 0) ToolTip.SetTip(seg, string.Join(", ", prof.Models));
            var pf = prof;
            seg.Click += () =>
            {
                foreach (var (sg, _) in _mechSegs) sg.On = false;
                seg.On = true; _mech = pf; _model = null;
                BuildModels(); UpdateLimit(); UpdateMarkers();
            };
            _mechSegs.Add((seg, prof));
            _segs.Add((seg, () => id == "none" ? Loc.T("wfs.mech.none") : id.StartsWith("sankyo") ? "Sankyo " + prof.Title[^2..] : id == "zx9" ? "Nakamichi ZX-9" : prof.Title));
            _mechPanel.Children.Add(seg);
        }
    }

    private void BuildModels()
    {
        _modelPanel.Children.Clear();
        _defModel = null;
        var specs = Specs.For(_mech.Id).ToList();
        if (specs.Count > 0 && specs.Select(q => q.WfMax).Distinct().Count() == 1) _model = specs[0];
        bool pick = specs.Count > 0 && _model == null;
        if (pick) _defModel = specs.GroupBy(q => q.WfMax).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().First();
        if (!pick) return;
        var lab = Ui.Label(); lab.Text = Loc.T("wfs.model"); lab.FontSize = 12; lab.Margin = new Thickness(0, 6, 10, 4); lab.VerticalAlignment = VerticalAlignment.Center;
        _modelPanel.Children.Add(lab);
        var group = new List<Seg>();
        foreach (var sp in specs)
        {
            var seg = new Seg(sp.Model) { Margin = new Thickness(0, 0, 4, 4) };
            ToolTip.SetTip(seg, $"max {sp.WfMax:0.000} % {sp.Standard}");
            var m = sp;
            seg.Click += () => { foreach (var g in group) g.On = false; seg.On = true; _model = m; UpdateLimit(); };
            group.Add(seg); _modelPanel.Children.Add(seg);
        }
    }

    private void UpdateLimit()
    {
        var m = _model ?? _defModel;
        bool on = m != null && _mode == 0 && m.Standard.StartsWith("WRMS", StringComparison.OrdinalIgnoreCase);
        _wfMeter.Limit = on ? m!.WfMax : double.NaN;
        _wfMeter.LimitLabel = on ? $"{Loc.T("wfs.limit")} {m!.WfMax:0.000}" : "";
        if (!on && _over) { _over = false; _wfValue.Foreground = Ui.GoldLight; }
        _wfMeter.InvalidateVisual();
    }

    private void UpdateMarkers()
    {
        bool sl = Loc.Lang == "sl";
        double ratio = _hz.Count > 0 ? _hz.Average(p => p.Y) / _refHz : 1.0;
        _specChart.Markers = _mech.Id == "none" ? null
            : _mech.Expected(ratio).Where(e => e.Hz > 0).Select(e => (e.Hz, $"{e.Hz:0.00} Hz · {(sl ? e.Sl : e.En)}")).ToList();
        _specChart.InvalidateVisual();
    }

    private void ApplyRange()
    {
        _wfMeter.Maximum = _wfRange;
        _wfMeter.MajorStep = _wfRange / 5;
        _wfMeter.LabelFormat = _wfRange >= 1 ? "0.0" : "0.00";
    }

    // ---------- zajem ----------
    private void Begin()
    {
        var dev = PaEngine.Resolve();
        if (dev == null)
        {
            _status.Text = Ui.Tt("Ni vhodne zvočne naprave. Priključi kartico in odpri Nastavitve.", "No audio input device. Connect a card and open Settings.")
                           + (PaEngine.LastError.Length > 0 ? "  (" + PaEngine.LastError + ")" : "");
            return;
        }
        _speed.Clear(); _wf.Clear(); _pk.Clear(); _hz.Clear(); _raw.Clear(); _specAvg = null; _noSigT = -1; _endText.Text = "";
        _speedShown = 0; _wfShown = 0; _ticks = 0;
        _cap ??= new PaCapture();
        _core = null;
        WfCore? core = null;
        string? err = _cap.Start(dev, AppSettings.InputStart, 2, (buf, n, c) =>
        {
            var co = core; if (co == null) return;
            if (_chan == 2 && c >= 2)
            {
                var mono = new float[n];
                for (int i = 0; i < n; i++) mono[i] = 0.5f * (buf[i * c] + buf[i * c + 1]);
                co.Process(mono, n, 1, 0);
            }
            else co.Process(buf, n, c, Math.Min(_chan, c - 1));
        });
        if (err != null) { _status.Text = Ui.Tt("Napaka zvočne kartice: ", "Audio device error: ") + err; return; }
        core = new WfCore(_cap.SampleRate, _refHz);
        _core = core;
        _source.Text = Ui.Tt("Vhod: ", "Input: ") + $"{_cap.DeviceName} · {PaEngine.ApiName} · {_cap.SampleRate / 1000.0:0.#} kHz";
        _status.Text = "";
        _sw.Restart(); _timer.Start(); _running = true;
        ApplyLang();
    }

    private void Stop()
    {
        _cap?.Stop(); _core = null;
        _level.Text = "";
        _timer.Stop(); _sw.Stop();
        _running = false;
        ApplyLang();
    }

    private void Tick()
    {
        var core = _core;
        if (core == null) return;
        double t = _sw.Elapsed.TotalSeconds;
        core.DrainRaw(_raw);
        var snap = core.Snapshot;
        _level.Text = snap.Settling ? Loc.T("wfs.settling") : snap.Valid ? $"{snap.LevelDb:0.0} dBFS" : Loc.T("wfs.nosig");
        bool valid = snap.Valid && !snap.Settling;
        double wf = _mode == 0 ? snap.Wrms : _mode == 1 ? snap.Wpeak : snap.Urms;

        if (valid) _noSigT = -1;
        else if (_wf.Count >= MinValidSec * 10)
        {
            if (_noSigT < 0) _noSigT = t;
            else if (t - _noSigT >= NoSignalStopSec)
            {
                Stop();
                _endText.Text = Loc.T("wfs.endtape").ToUpperInvariant();
                return;
            }
        }

        var inv = CultureInfo.InvariantCulture;
        if (valid)
        {
            if (_ticks % 3 == 0)
            {
                _speed.Add(new WPoint(t, snap.SpeedPct));
                _hz.Add(new WPoint(t, _refHz * (1 + snap.SpeedPct / 100)));
                _wf.Add(new WPoint(t, snap.Wrms));
                _pk.Add(new WPoint(t, snap.Wpeak));
            }
            _speedShown += (snap.SpeedPct - _speedShown) * 0.09;
            _wfShown += (wf - _wfShown) * 0.054;
            _speedMeter.Value = _speedShown; _wfMeter.Value = _wfShown;
            _speedHz.Text = (_refHz * (1 + _speedShown / 100)).ToString("0.0", inv) + "  Hz";
            _speedPct.Text = _speedShown.ToString("+0.00;-0.00;0.00", inv) + "  %";
            _wfValue.Text = _wfShown.ToString("0.000", inv) + "  %";
            bool over = !double.IsNaN(_wfMeter.Limit) && _wfShown > _wfMeter.Limit;
            if (over != _over) { _over = over; _wfValue.Foreground = over ? Ui.Red : Ui.GoldLight; }
        }
        else { _speedHz.Text = "----  Hz"; _speedPct.Text = "----  %"; _wfValue.Text = "-.---  %"; }

        var el = TimeSpan.FromSeconds(t);
        _elapsed.Text = $"{(int)el.TotalHours:00}:{el.Minutes:00}:{el.Seconds:00}";
        RedrawTime(t);
        if (++_ticks % 15 == 0) RedrawSpectrum();
        if (t >= _durationMin * 60) Stop();
    }

    // ---------- grafi ----------
    private const double Fs = WfCore.OutFs;
    private void Redraw(double t) { RedrawTime(t); RedrawSpectrum(); }

    private void RedrawTime(double t)
    {
        double w = _window;
        double x1 = Math.Max(t, w), x0 = x1 - w;
        var view = new List<WPoint>();
        double maxAbs = 0.1;
        if (_raw.Count > 0)
        {
            int i1 = _raw.Count, i0 = Math.Max(0, i1 - (int)(w * Fs));
            double mean = 0; for (int i = i0; i < i1; i++) mean += _raw[i];
            mean /= Math.Max(1, i1 - i0);
            for (int i = i0; i < i1; i++)
            {
                double y = _raw[i] - mean;
                view.Add(new WPoint(i / Fs, y));
                maxAbs = Math.Max(maxAbs, Math.Abs(y));
            }
        }
        double ymax = NiceCeil(maxAbs * 1.15);
        _timeChart.SetData(view, x0, x1, -ymax, ymax);
    }

    private void RedrawSpectrum()
    {
        var spec = Spectrum.Welch(_raw, Fs, _specWin, 0.5, 0.2, 100.0, 4);
        int key = spec.Count * 1000 + (int)_specWin;
        if (spec.Count > 0)
        {
            if (_specAvg == null || _specAvg.Length != spec.Count || _specKey != key)
            { _specAvg = spec.Select(q => q.Y).ToArray(); _specKey = key; }
            else for (int i = 0; i < spec.Count; i++) _specAvg[i] = _specAvg[i] * 0.65 + spec[i].Y * 0.35;
            spec = spec.Select((q, i) => new WPoint(q.X, _specAvg![i])).ToList();
        }
        UpdatePeriodicity(spec);
        UpdateMarkers();
        _specChart.XTicks = new[] { 0.2, 0.4, 0.6, 0.8, 1, 2, 4, 6, 8, 10, 20, 40, 60, 80, 100 };
        _specChart.SetData(spec, 0.2, 100, 0, _wfRange);
    }

    private void UpdatePeriodicity(List<WPoint> spec)
    {
        var cand = spec.Where(q => q.X >= 0.5).ToList();
        if (cand.Count < 10) { _period.Text = ""; return; }
        var ys = cand.Select(q => q.Y).OrderBy(v => v).ToList();
        double median = ys[ys.Count / 2];
        var top = cand.OrderByDescending(q => q.Y).First();
        _period.Text = top.Y < 0.004 || top.Y < 5 * median
            ? Loc.T("wfs.per.none")
            : $"{Loc.T("wfs.per")}: {top.X:0.00} Hz · {top.Y:0.000} %";
    }

    private static double NiceCeil(double v)
    {
        foreach (double s in new[] { 0.1, 0.2, 0.5, 1, 2, 3, 5, 10 })
            if (v <= s) return s;
        return Math.Ceiling(v);
    }

    // ---------- izvoz ----------
    private void ExportPng()
    {
        try
        {
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            if (size.Width < 10 || size.Height < 10) return;
            using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(this);
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"WFS Screen {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            rtb.Save(file);
            _status.Text = Loc.T("wfs.saved") + file;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void ExportCsv()
    {
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("time_s;speed_hz;speed_pct;wrms_pct;peak_pct\n");
            int cnt = Math.Min(Math.Min(_speed.Count, _hz.Count), Math.Min(_wf.Count, _pk.Count));
            for (int i = 0; i < cnt; i++)
                sb.AppendLine(string.Join(';', _speed[i].X.ToString("0.000", inv), _hz[i].Y.ToString("0.0", inv),
                    _speed[i].Y.ToString("0.0000", inv), _wf[i].Y.ToString("0.0000", inv), _pk[i].Y.ToString("0.0000", inv)));
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"WFS Screen {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
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
