using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CDSMeasure.Audio;
using CDSMeasure.Controls;
using CDSMeasure.Core;

namespace CDSMeasure.Views;

/// <summary>Playback frekvenčni odziv: sweep s kasete → nivo v pasovih 1/3 oktave (L modro, D rdeče).</summary>
public sealed class PbView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private PaCapture? _cap;
    private PbRecorder? _rec;
    private PbCore? _core;
    private bool _running, _busy, _stepsMode, _norm, _peak = true;
    private double _span = 50;
    private string _source = "";
    private double _lastGrow, _offs, _allL = double.NaN, _allR = double.NaN;
    private bool _allNorm, _allPeak;
    private int _liveMax = -1, _backCount, _lastCount;
    private double _liveL = double.NaN, _liveR = double.NaN;
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private readonly CdsButton _start = new(true), _import = new(), _shot = new(), _csv = new();
    private readonly TextBlock _elapsed = new(), _level = new(), _status = new(), _src = new();
    private readonly BandChart _chart = new();
    private readonly List<(TextBlock Tb, string Key)> _labels = new();
    private readonly List<(Seg Seg, string Key)> _segs = new();

    public PbView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 14, 4);
        _start.Click += () => { if (_running) StopMeasure(true); else StartMeasure(); };
        _elapsed.FontFamily = Ui.Mono; _elapsed.FontSize = 20; _elapsed.Foreground = Ui.Text;
        _elapsed.VerticalAlignment = VerticalAlignment.Center; _elapsed.Margin = new Thickness(0, 0, 14, 4); _elapsed.Text = "00:00";
        _import.Margin = new Thickness(0, 0, 18, 4); _import.Click += ImportFile;
        bar.Children.Add(_start); bar.Children.Add(_elapsed); bar.Children.Add(_import);

        AddGroup(bar, "pb.mode", v => _stepsMode = v > 0, 0, ("pb.sweep", 0), ("pb.steps", 1));
        AddGroup(bar, "pb.calc", v => { _peak = v > 0; UpdateChart(); }, 1, ("pb.peak", 1), ("pb.mean", 0));
        AddGroup(bar, "pb.ref", v => { _norm = v > 0; UpdateChart(); }, 0, ("pb.abs", 0), ("pb.norm", 1));
        AddGroup(bar, "pb.range", v => { _span = v; UpdateChart(); }, 50, ("+10 … −40 dB", 50), ("20 dB", 20), ("40 dB", 40), ("Auto", 0));

        _level.FontFamily = Ui.Mono; _level.FontSize = 13; _level.Foreground = Ui.Gold;
        _level.VerticalAlignment = VerticalAlignment.Center; _level.Margin = new Thickness(0, 0, 18, 4);
        bar.Children.Add(_level);
        _shot.Margin = new Thickness(6, 0, 0, 4); _shot.Click += ExportPng;
        _csv.Margin = new Thickness(6, 0, 0, 4); _csv.Click += ExportCsv;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(_shot); actions.Children.Add(_csv);
        var top = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions); top.Children.Add(bar);
        Grid.SetRow(top, 0); root.Children.Add(top);

        var panel = Ui.PanelBox(_chart, new Thickness(6));
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _src.FontSize = 12; _src.Foreground = Ui.Dim;
        _status.FontSize = 12; _status.Foreground = Ui.Gold;
        Grid.SetColumn(_status, 1);
        foot.Children.Add(_src); foot.Children.Add(_status);
        Grid.SetRow(foot, 2); root.Children.Add(foot);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        UpdateChart();
    }

    private void AddGroup(WrapPanel host, string labelKey, Action<double> on, double def, params (string Key, double Val)[] items)
    {
        var tb = Ui.Label(); tb.Margin = new Thickness(0, 0, 8, 4);
        _labels.Add((tb, labelKey)); host.Children.Add(tb);
        var group = new List<Seg>();
        foreach (var (key, val) in items)
        {
            var seg = new Seg(key) { On = val == def };
            double v = val;
            seg.Click += () => { foreach (var s in group) s.On = false; seg.On = true; on(v); };
            group.Add(seg); host.Children.Add(seg); _segs.Add((seg, key));
        }
        host.Children.Add(new Border { Width = 18, Height = 1 });
    }

    private void ApplyLang()
    {
        _start.Text = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _import.Text = Loc.T("pb.import");
        _shot.Text = Loc.T("wfs.shot"); _csv.Text = Loc.T("wfs.csv");
        foreach (var (tb, key) in _labels) tb.Text = Loc.T(key);
        foreach (var (seg, key) in _segs) seg.Text = key.StartsWith("pb.") ? Loc.T(key) : key;
        if (!_running && _source.Length == 0) _src.Text = Ui.Tt("Vhod: ", "Input: ") + PaEngine.CardText();
        _chart.Refresh();
    }

    // ---------- zajem ----------
    private void StartMeasure()
    {
        if (_busy) return;
        var dev = PaEngine.Resolve();
        if (dev == null)
        {
            _status.Text = Ui.Tt("Ni vhodne zvočne naprave. Priključi kartico in odpri Nastavitve.", "No audio input device. Connect a card and open Settings.")
                           + (PaEngine.LastError.Length > 0 ? "  (" + PaEngine.LastError + ")" : "");
            return;
        }
        _rec?.Dispose(); _rec = null; _core = null;
        _cap ??= new PaCapture();
        PbRecorder? rec = null;
        string? err = _cap.Start(dev, AppSettings.InputStart, 2, (buf, n, c) => rec?.Feed(buf, n, c));
        if (err != null) { _status.Text = Ui.Tt("Napaka zvočne kartice: ", "Audio device error: ") + err; return; }
        _core = new PbCore(_cap.SampleRate, _cap.Channels) { StepsMode = _stepsMode };
        rec = new PbRecorder(_core);
        _rec = rec;
        _source = $"{_cap.DeviceName} · {PaEngine.ApiName} · {_cap.SampleRate / 1000.0:0.#} kHz";
        _src.Text = Ui.Tt("Vhod: ", "Input: ") + _source;
        _lastCount = 0; _lastGrow = 0; _allL = _allR = double.NaN; _liveMax = -1; _backCount = 0;
        _sw.Restart(); _timer.Start(); _running = true;
        _status.Text = Loc.T("pb.waiting");
        ApplyLang();
        UpdateChart();
    }

    private void StopMeasure(bool manual)
    {
        _cap?.Stop();
        _rec?.Stop();
        _timer.Stop(); _sw.Stop();
        _running = false;
        _level.Text = "";
        UpdateChart();
        int n = _core?.PointCount ?? 0;
        _status.Text = n >= 10 ? Loc.T("pb.done") : Loc.T("pb.noresp");
        ApplyLang();
        if (!manual && n >= 10) ExportPng();
    }

    private void Tick()
    {
        var core = _core;
        if (core == null) return;
        double t = _sw.Elapsed.TotalSeconds;
        _elapsed.Text = $"{(int)(t / 60):00}:{(int)t % 60:00}";
        _level.Text = core.Channels >= 2
            ? $"L {core.PeakDb(0):0.0}  R {core.PeakDb(1):0.0} dBFS"
            : $"{core.PeakDb(0):0.0} dBFS";
        int count = core.PointCount;
        if (count > _lastCount) { _lastCount = count; _lastGrow = t; }
        if (count > 0) _status.Text = Loc.T("pb.measuring");
        UpdateChart();
        if (count >= 30 && t - _lastGrow >= 8) StopMeasure(false);
    }

    // ---------- uvoz WAV ----------
    private async void ImportFile()
    {
        if (_busy || _running) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("WAV") { Patterns = new[] { "*.wav", "*.WAV" } } },
        });
        if (files.Count == 0) return;
        string path = files[0].Path.LocalPath;
        _busy = true; _status.Text = Loc.T("pb.analysing");
        bool steps = _stepsMode;
        try
        {
            var core = await Task.Run(() => PbFile.Analyze(path, steps));
            _core = core; _allL = _allR = double.NaN;
            _source = System.IO.Path.GetFileName(path);
            _src.Text = _source;
            _elapsed.Text = $"{(int)(core.Seconds / 60):00}:{(int)core.Seconds % 60:00}";
            UpdateChart();
            _status.Text = core.PointCount >= 10 ? Loc.T("pb.done") : Loc.T("pb.noresp");
        }
        catch (Exception ex) { _status.Text = Loc.T("pb.err") + ex.Message; }
        finally { _busy = false; }
    }

    // ---------- prikaz ----------
    private (List<BandChart.Series> S, double Top, double Span, string YTitle) Build()
    {
        Func<FrPoint[], double[]> band = _peak ? PbCore.BandsPeak : PbCore.Bands;
        var list = new List<BandChart.Series>();
        var core = _core;
        double mx = double.NegativeInfinity, mn = double.PositiveInfinity;
        if (core != null && core.PointCount > 0)
        {
            double[] l = band(core.Points(0));
            double[]? r = core.Channels >= 2 ? band(core.Points(1)) : null;
            _offs = 0;
            if (_norm)
            {
                double a = l[PbCore.RefBand], b = r != null ? r[PbCore.RefBand] : double.NaN;
                double offs = !double.IsNaN(a) && !double.IsNaN(b) ? 0.5 * (a + b) : !double.IsNaN(a) ? a : !double.IsNaN(b) ? b : 0;
                _offs = offs;
                for (int i = 0; i < l.Length; i++) l[i] -= offs;
                if (r != null) for (int i = 0; i < r.Length; i++) r[i] -= offs;
            }
            foreach (var v in l.Concat(r ?? Array.Empty<double>()))
                if (!double.IsNaN(v)) { if (v > mx) mx = v; if (v < mn) mn = v; }
            if (_allNorm != _norm || _allPeak != _peak) { _allNorm = _norm; _allPeak = _peak; _allL = _allR = double.NaN; }
            foreach (var v in l) if (!double.IsNaN(v) && (double.IsNaN(_allL) || v > _allL)) _allL = v;
            if (r != null) foreach (var v in r) if (!double.IsNaN(v) && (double.IsNaN(_allR) || v > _allR)) _allR = v;
            list.Add(new BandChart.Series { L = l, R = r, Label = "" });
        }
        bool any = !double.IsInfinity(mx);
        double top, span = _span;
        if (_span >= 50) top = 10;
        else if (_span == 0)
        {
            if (any) { top = Math.Ceiling((mx + 3) / 5) * 5; span = Math.Max(10, top - Math.Floor((mn - 3) / 5) * 5); }
            else { top = _norm ? 5 : 0; span = 20; }
        }
        else top = _norm ? 5 : (any ? Math.Ceiling((mx + 2) / 5) * 5 : 0);
        return (list, top, span, _norm ? "dB" : "SPL [dB]");
    }

    private void SetLive()
    {
        var core = _core;
        double t = _sw.Elapsed.TotalSeconds;
        if (!_running || core == null || t - _lastGrow > 1.5)
        {
            _chart.LiveBand = -1; _chart.LiveL = double.NaN; _chart.LiveR = double.NaN;
            return;
        }
        double bestT = double.NegativeInfinity; int band = -1;
        double lv0 = double.NaN, lv1 = double.NaN;
        for (int c = 0; c < core.Channels && c < 2; c++)
        {
            var pts = core.Points(c);
            if (pts.Length == 0) continue;
            var p = pts[pts.Length - 1];
            if (c == 0) lv0 = p.Db - _offs; else lv1 = p.Db - _offs;
            if (p.T >= bestT) { bestT = p.T; band = BandOf(p.F); }
        }
        if (band >= _liveMax - 1) { _backCount = 0; if (band >= _liveMax) _liveMax = band; _liveL = lv0; _liveR = lv1; }
        else if (++_backCount >= 4) { _backCount = 0; _liveMax = band; _liveL = lv0; _liveR = lv1; }
        _chart.LiveBand = _liveMax;
        _chart.LiveL = _liveL; _chart.LiveR = _liveR;
    }

    private static int BandOf(double f)
    {
        for (int i = 0; i < PbCore.Centers.Length; i++)
            if (f >= PbCore.Centers[i] * Math.Pow(10, -0.05) && f < PbCore.Centers[i] * Math.Pow(10, 0.05)) return i;
        return -1;
    }

    private void UpdateChart()
    {
        var (s, top, span, yt) = Build();
        _chart.ShowRight = _core == null || _core.Channels >= 2;
        _chart.YTitle = yt;
        _chart.MaxL = _allL; _chart.MaxR = _allR;
        SetLive();
        _chart.SetSeries(s, top, span);
    }

    // ---------- izvoz ----------
    private void ExportPng()
    {
        try
        {
            var (s, top, span, yt) = Build();
            if (s.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
            const int pw = 1600, ph = 800;
            var ch = new BandChart { Width = pw, Height = ph, YTitle = yt, ShowRight = _chart.ShowRight, Caption = $"{_source}, {DateTime.Now:dd.MM.yyyy}" };
            ch.MaxL = _allL; ch.MaxR = _allR;
            ch.SetSeries(s, top, span);
            ch.Measure(new Size(pw, ph)); ch.Arrange(new Rect(0, 0, pw, ph));
            using var rtb = new RenderTargetBitmap(new PixelSize(pw, ph), new Vector(96, 96));
            rtb.Render(ch);
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"PB.Freq.Resp {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            rtb.Save(file);
            _status.Text = Loc.T("wfs.saved") + file;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void ExportCsv()
    {
        try
        {
            var (s, _, _, _) = Build();
            if (s.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
            string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
            var x = s[0];
            var sb = new StringBuilder(x.R != null ? "band_hz;left;right\n" : "band_hz;left\n");
            for (int i = 0; i < PbCore.Centers.Length; i++)
                sb.AppendLine(string.Join(';', x.R != null ? new[] { PbCore.Labels[i], F(x.L![i]), F(x.R[i]) } : new[] { PbCore.Labels[i], F(x.L![i]) }));
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"PB.Freq.Resp {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv");
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
        _rec?.Dispose();
    }
}
