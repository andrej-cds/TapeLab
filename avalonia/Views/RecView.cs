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

namespace CDSMeasure.Views;

/// <summary>Recording frekvenčni odziv: program odda sweep v kasetofon, signal z monitorja se zajema na vhodu.
/// Vsaka meritev (−20, −10, 0, +6 dB) ostane na istem grafu z oznako nivoja ob krivulji.</summary>
public sealed class RecView : UserControl, IDisposable
{
    private static double OutRefDbfs => AppSettings.SweepRefDbfs;
    private const double F0 = 10, F1Max = 30000;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private PaDuplex? _dup;
    private SweepSynth? _gen;
    private bool _running, _autoRange;
    private double _dur = 10, _lvl = -20, _f1 = F1Max;
    private int _chans = 2;
    private string _label = "", _source = "";
    private readonly System.Diagnostics.Stopwatch _sw = new();
    private readonly object _lock = new();
    private double _peakL, _peakR;
    private readonly List<(string Label, double Lvl, int Id, double[] F, double[] L, double[]? FR, double[]? R)> _done = new();
    private PbCore? _live;
    private PbRecorder? _liveRec;
    private int _nextId, _liveId;
    private readonly Dictionary<int, (Color L, Color R)> _cols = new();

    private readonly CdsButton _start = new(true), _clear = new(), _all = new(), _shot = new(), _csv = new();
    private readonly TextBlock _elapsed = new(), _level = new(), _status = new(), _src = new();
    private readonly FrChart _chart = new();
    private readonly List<(TextBlock Tb, string Key)> _labels = new();
    private readonly List<(Seg Seg, string Key)> _segs = new();
    private readonly Dictionary<(string Group, double Val), Seg> _segMap = new();

    private readonly Queue<double> _seq = new();
    private bool _seqActive, _seqWait;
    private const int SeqPauseSec = 5;
    private int _seqLeft;
    private readonly DispatcherTimer _seqTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private static string LvlText(double v) => v > 0 ? $"+{v:0} dB" : v < 0 ? $"−{-v:0} dB" : "0 dB";

    public RecView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 14, 4);
        _start.Click += () => { if (_running) Finish(false); else { CancelSeq(); StartMeasure(); } };
        _elapsed.FontFamily = Ui.Mono; _elapsed.FontSize = 20; _elapsed.Foreground = Ui.Text;
        _elapsed.VerticalAlignment = VerticalAlignment.Center; _elapsed.Margin = new Thickness(0, 0, 18, 4); _elapsed.Text = "00:00";
        _all.Margin = new Thickness(0, 0, 18, 4); _all.Click += OnAll;
        bar.Children.Add(_start); bar.Children.Add(_all); bar.Children.Add(_elapsed);

        AddGroup(bar, "rec.lvl", v => _lvl = v, -20, ("−20 dB", -20), ("−10 dB", -10), ("0 dB", 0), ("+6 dB", 6));
        AddGroup(bar, "rec.dur", v => _dur = v, 10, ("10 s", 10), ("20 s", 20), ("30 s", 30), ("60 s", 60));
        AddGroup(bar, "rec.range", v => { _autoRange = v > 0; UpdateChart(); }, 0, ("+10 … −40 dB", 0), ("Auto", 1));

        _clear.Margin = new Thickness(0, 0, 18, 4);
        _clear.Click += () => { if (!_running) { _done.Clear(); _cols.Clear(); UpdateChart(); } };
        bar.Children.Add(_clear);
        _level.FontFamily = Ui.Mono; _level.FontSize = 13; _level.Foreground = Ui.Gold;
        _level.VerticalAlignment = VerticalAlignment.Center; _level.Margin = new Thickness(0, 0, 18, 4);
        bar.Children.Add(_level);
        _shot.Margin = new Thickness(0, 0, 8, 4); _shot.Click += ExportPng;
        _csv.Margin = new Thickness(0, 0, 0, 4); _csv.Click += ExportCsv;
        bar.Children.Add(_shot); bar.Children.Add(_csv);
        Grid.SetRow(bar, 0); root.Children.Add(bar);

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
        _seqTimer.Tick += (_, _) =>
        {
            if (!_seqWait || !_seqActive) { _seqTimer.Stop(); return; }
            if (--_seqLeft <= 0) { _seqTimer.Stop(); OnAll(); }
            else _status.Text = Loc.T("rec.seqwait") + LvlText(_seq.Peek()) + $"  ({_seqLeft} s)";
        };
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
            group.Add(seg); host.Children.Add(seg); _segs.Add((seg, key)); _segMap[(labelKey, val)] = seg;
        }
        host.Children.Add(new Border { Width = 18, Height = 1 });
    }

    private void SelectSeg(string group, double val)
    {
        foreach (var kv in _segMap) if (kv.Key.Group == group) kv.Value.On = kv.Key.Val == val;
    }

    private void ApplyLang()
    {
        _start.Text = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _clear.Text = Loc.T("rec.clear");
        _all.Enabled = !_running;
        _all.Text = _seqWait && _seq.Count > 0 ? Loc.T("rec.next") + LvlText(_seq.Peek()) : Loc.T("rec.all");
        _shot.Text = Loc.T("wfs.shot"); _csv.Text = Loc.T("wfs.csv");
        foreach (var (tb, key) in _labels) tb.Text = Loc.T(key);
        foreach (var (seg, key) in _segs) seg.Text = key.StartsWith("rec.") ? Loc.T(key) : key;
        if (!_running && _source.Length == 0) _src.Text = Ui.Tt("Vhod/izhod: ", "Input/output: ") + PaEngine.CardText();
        _chart.Refresh();
    }

    // ---------- zaporedje vseh 4 meritev ----------
    private void CancelSeq() { _seqTimer.Stop(); _seq.Clear(); _seqActive = false; _seqWait = false; ApplyLang(); }

    private void OnAll()
    {
        if (_running) return;
        _seqTimer.Stop();
        if (!_seqActive)
        {
            _done.Clear(); _cols.Clear(); _seq.Clear();
            foreach (var l in new[] { -20.0, -10.0, 0.0, 6.0 }) _seq.Enqueue(l);
            _seqActive = true;
            _dur = 60; SelectSeg("rec.dur", 60);
        }
        _seqWait = false;
        _lvl = _seq.Dequeue();
        SelectSeg("rec.lvl", _lvl);
        UpdateChart();
        StartMeasure();
        if (!_running) CancelSeq();
    }

    // ---------- meritev ----------
    private void StartMeasure()
    {
        if (_running) return;
        var dev = PaEngine.Resolve();
        if (dev == null)
        {
            _status.Text = Ui.Tt("Ni zvočne naprave. Priključi kartico in odpri Nastavitve.", "No audio device. Connect a card and open Settings.")
                           + (PaEngine.LastError.Length > 0 ? "  (" + PaEngine.LastError + ")" : "");
            return;
        }
        _label = LvlText(_lvl);
        _liveId = ++_nextId;
        _cols[_liveId] = DefaultCols(_done.Count(d => d.Lvl == _lvl));
        _gen = null;
        lock (_lock) _peakL = _peakR = 0;
        _liveRec?.Dispose();
        _dup ??= new PaDuplex();
        string? err = _dup.Start(dev, AppSettings.InputStart, AppSettings.OutputStart,
            rate =>
            {
                _f1 = Math.Min(F1Max, 0.45 * rate);
                _gen = new SweepSynth(rate, F0, _f1, _dur, _lvl + OutRefDbfs);
                return _gen;
            },
            (rate, ch) =>
            {
                _chans = Math.Clamp(ch, 1, 2);
                _live = new PbCore(rate, _chans);
                var liveRec = _liveRec = new PbRecorder(_live);
                int chans = _chans;
                return (buf, n, c) =>
                {
                    liveRec.Feed(buf, n, c);
                    double pl = 0, pr = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double a = Math.Abs(buf[i * c]); if (a > pl) pl = a;
                        if (c > 1) { double b = Math.Abs(buf[i * c + 1]); if (b > pr) pr = b; }
                    }
                    lock (_lock) { _peakL = pl; _peakR = pr; }
                };
            });
        if (err != null) { _status.Text = Ui.Tt("Napaka zvočne kartice: ", "Audio device error: ") + err; return; }
        _source = $"Recording · {_dup.DeviceName} · {PaEngine.ApiName} · {_dup.SampleRate / 1000.0:0.#} kHz";
        _src.Text = _source;
        _sw.Restart(); _timer.Start(); _running = true;
        _status.Text = Loc.T("rec.run");
        ApplyLang();
    }

    private void Tick()
    {
        double t = _sw.Elapsed.TotalSeconds;
        _elapsed.Text = $"{(int)(t / 60):00}:{(int)t % 60:00}";
        double pl, pr;
        lock (_lock) { pl = _peakL; pr = _peakR; }
        string L(double v) => (20 * Math.Log10(Math.Max(v, 1e-9))).ToString("0.0", CultureInfo.InvariantCulture);
        _level.Text = _chans >= 2 ? $"IN L {L(pl)}  R {L(pr)} dBFS" : $"IN {L(pl)} dBFS";
        UpdateChart();
        if (_gen != null && _gen.Finished && t > _gen.Seconds + 3) Finish(true);
    }

    private static FrPoint[] Clean(FrPoint[] pts)
    {
        var keep = new List<FrPoint>(pts.Length);
        double runMax = 0;
        foreach (var p in pts)
        {
            if (p.F < runMax * 0.9) continue;
            keep.Add(p);
            if (p.F > runMax) runMax = p.F;
        }
        return keep.OrderBy(p => p.F).ToArray();
    }

    private void Finish(bool auto)
    {
        if (!_running) return;
        _timer.Stop(); _sw.Stop();
        try { _dup?.Stop(); } catch { }
        _running = false; _level.Text = "";
        try { _liveRec?.Stop(); } catch { }
        ApplyLang();

        var live = _live;
        if (live == null || live.PointCount < 10) { CancelSeq(); _status.Text = Loc.T("pb.noresp"); return; }
        var p0 = Clean(live.Points(0));
        var p1 = live.Channels >= 2 ? Clean(live.Points(1)) : null;
        _done.Add((_label, _lvl, _liveId,
                   p0.Select(p => p.F).ToArray(), p0.Select(p => p.Db).ToArray(),
                   p1?.Select(p => p.F).ToArray(), p1?.Select(p => p.Db).ToArray()));
        var sorted = _done.OrderBy(x => x.Lvl).ThenBy(x => x.Id).ToList();
        _done.Clear(); _done.AddRange(sorted);
        _live = null;
        UpdateChart();
        _status.Text = (auto ? Loc.T("rec.done") : Loc.T("rec.stopped")) + $" ({_label})";
        if (!auto) CancelSeq();
        if (auto && _seqActive && _seq.Count > 0)
        {
            _seqWait = true;
            _seqLeft = SeqPauseSec;
            _status.Text = Loc.T("rec.seqwait") + LvlText(_seq.Peek()) + $"  ({_seqLeft} s)";
            _seqTimer.Stop(); _seqTimer.Start();
            ApplyLang();
        }
        else
        {
            CancelSeq();
            if (auto) ExportPng();
        }
    }

    // ---------- prikaz ----------
    private static (Color L, Color R) DefaultCols(int k) => (k % 3) switch
    {
        0 => (Color.FromRgb(0x4D, 0xA3, 0xFF), Color.FromRgb(0xE3, 0x00, 0x1B)),
        1 => (Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF)),
        _ => (Color.FromRgb(0x80, 0xFF, 0x00), Color.FromRgb(0xFF, 0x80, 0x00)),
    };
    private (Color L, Color R) Cols(int id) => _cols.TryGetValue(id, out var c) ? c : DefaultCols(0);

    private List<FrChart.Series> BuildSeries()
    {
        var list = new List<FrChart.Series>();
        foreach (var d in _done)
            list.Add(new FrChart.Series { F = d.F, L = d.L, FR = d.FR, R = d.R, Label = d.Label, CL = Cols(d.Id).L, CR = Cols(d.Id).R });
        var live = _live;
        if (_running && live != null && live.PointCount > 0)
        {
            var p0 = Clean(live.Points(0));
            var s = new FrChart.Series { F = p0.Select(p => p.F).ToArray(), L = p0.Select(p => p.Db).ToArray(), Label = _label, CL = Cols(_liveId).L, CR = Cols(_liveId).R };
            if (live.Channels >= 2)
            {
                var p1 = Clean(live.Points(1));
                if (p1.Length > 0) { s.FR = p1.Select(p => p.F).ToArray(); s.R = p1.Select(p => p.Db).ToArray(); }
            }
            list.Add(s);
        }
        return list;
    }

    private void UpdateChart() => _chart.SetSeries(BuildSeries(), _autoRange);

    // ---------- izvoz ----------
    private void ExportPng()
    {
        try
        {
            if (_done.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
            const int pw = 1600, ph = 800;
            var ch = new FrChart { Width = pw, Height = ph, Caption = $"{_source}, {DateTime.Now:dd.MM.yyyy}" };
            ch.SetSeries(BuildSeries(), _autoRange);
            ch.Measure(new Size(pw, ph)); ch.Arrange(new Rect(0, 0, pw, ph));
            using var rtb = new RenderTargetBitmap(new PixelSize(pw, ph), new Vector(96, 96));
            rtb.Render(ch);
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"REC.Freq.Resp {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            rtb.Save(file);
            _status.Text = Loc.T("wfs.saved") + file;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void ExportCsv()
    {
        try
        {
            if (_done.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
            string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
            var head = new List<string> { "freq_hz" };
            foreach (var d in _done)
            {
                int n = _done.TakeWhile(x => x.Id != d.Id).Count(x => x.Label == d.Label);
                string sfx = "_" + d.Label.Replace(" ", "") + (n > 0 ? "_" + (n + 1) : "");
                head.Add("left" + sfx); if (d.R != null) head.Add("right" + sfx);
            }
            var sb = new StringBuilder(string.Join(';', head) + "\n");
            var cols = new List<double[]>();
            foreach (var d in _done)
            {
                cols.Add(Resample(d.F, d.L));
                if (d.R != null) cols.Add(Resample(d.FR ?? d.F, d.R));
            }
            for (int i = 0; i < RecAnalysis.Grid.Length; i++)
            {
                var row = new List<string> { RecAnalysis.Grid[i].ToString("0.0", CultureInfo.InvariantCulture) };
                foreach (var c in cols) row.Add(F(c[i]));
                sb.AppendLine(string.Join(';', row));
            }
            string file = System.IO.Path.Combine(AppSettings.OutDir(), $"REC.Freq.Resp {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            _status.Text = Loc.T("wfs.saved") + file;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private static double[] Resample(double[] f, double[] v)
    {
        var o = new double[RecAnalysis.Grid.Length];
        double half = Math.Log(2) / 48;
        for (int g = 0; g < o.Length; g++)
        {
            double sum = 0; int n = 0;
            double lg = Math.Log(RecAnalysis.Grid[g]);
            for (int i = 0; i < f.Length && i < v.Length; i++)
                if (Math.Abs(Math.Log(f[i]) - lg) <= half) { sum += v[i]; n++; }
            o[g] = n > 0 ? sum / n : double.NaN;
        }
        return o;
    }

    public void Dispose()
    {
        Loc.Changed -= ApplyLang;
        _timer.Stop(); _seqTimer.Stop();
        try { _dup?.Stop(); } catch { }
        _liveRec?.Dispose();
    }
}
