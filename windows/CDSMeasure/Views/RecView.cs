using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CDSMeasure.Controls;
using CDSMeasure.Core;
using NAudio.Wave;

namespace CDSMeasure.Views;

/// <summary>
/// Recording frekvenčni odziv: program odda sweep (ASIO izhod) v kasetofon, signal z monitorja se zajema na vhodu.
/// Vsaka meritev (nivo −20, −10, 0, +6 dB) ostane na istem grafu z oznako nivoja ob krivulji.
/// </summary>
public sealed class RecView : UserControl, IDisposable
{
    private static double OutRefDbfs => AppSettings.SweepRefDbfs;   // nivo 0 dB = −6 dBFS (privzeto; −20 dB = −26 dBFS, +6 dB = polna skala); nastavljivo v Nastavitvah
    private const double F0 = 10, F1Max = 30000;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AsioCapture? _asio;
    private string? _driver;
    private SweepGen? _gen;
    private bool _running;
    private double _dur = 10, _lvl = -20, _f1 = F1Max;
    private int _rate, _chans = 2;
    private string _label = "", _source = "";
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private readonly List<float[]>[] _chunks = { new(), new() };
    private readonly object _lock = new();
    private double _peakL, _peakR;
    private readonly List<(string Label, double Lvl, int Id, double[] F, double[] L, double[]? FR, double[]? R)> _done = new();

    // predogled v živo (sledenje tonu); po koncu ga nadomesti natančna analiza znanega sweepa
    private PbCore? _live;
    private PbRecorder? _liveRec;

    private WaveFileWriter? _wav;
    private readonly object _wavLock = new();
    private string? _wavPath;

    private readonly Button _start = new(), _clear = new(), _colors = new(), _all = new(), _shot = new(), _csv = new();
    private readonly CheckBox _saveWav = new();
    private readonly TextBlock _elapsed = new(), _level = new(), _status = new();
    private readonly FrChart _chart = new();
    private readonly List<(RadioButton Rb, string Key)> _radios = new();
    private readonly List<(TextBlock Tb, string Key)> _labels = new();

    private static object Res(string key) => Application.Current.Resources[key];
    private static string LvlText(double v) => v > 0 ? $"+{v:0} dB" : v < 0 ? $"−{-v:0} dB" : "0 dB";

    public RecView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.Style = (Style)Res("CdsPrimary"); _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 14, 4);
        _start.Click += (_, _) => { if (_running) Finish(false); else { CancelSeq(); StartMeasure(); } };
        _elapsed.FontFamily = (FontFamily)Res("FontMono"); _elapsed.FontSize = 20; _elapsed.Foreground = (Brush)Res("TextBrush");
        _elapsed.VerticalAlignment = VerticalAlignment.Center; _elapsed.Margin = new Thickness(0, 0, 18, 4); _elapsed.Text = "00:00";
        _all.Style = (Style)Res("CdsButton"); _all.Margin = new Thickness(0, 0, 18, 4);
        _all.Click += (_, _) => OnAll();
        bar.Children.Add(_start); bar.Children.Add(_all); bar.Children.Add(_elapsed);

        AddGroup(bar, "rec.lvl", v => { _lvl = (double)v; }, ("−20 dB", -20.0), ("−10 dB", -10.0), ("0 dB", 0.0), ("+6 dB", 6.0));
        AddGroup(bar, "rec.dur", v => { _dur = (double)v; }, ("10 s", 10.0), ("20 s", 20.0), ("30 s", 30.0), ("60 s", 60.0));
        AddGroup(bar, "rec.range", v => { _autoRange = (bool)v; _chart.SetSeries(BuildSeries(), _autoRange); }, ("+10 … −40 dB", false), ("Auto", true));

        _clear.Style = (Style)Res("CdsButton"); _clear.Margin = new Thickness(0, 0, 18, 4);
        _clear.Click += (_, _) => { if (!_running) { _done.Clear(); _cols.Clear(); UpdateChart(); } };
        bar.Children.Add(_clear);
        _colors.Style = (Style)Res("CdsButton"); _colors.Margin = new Thickness(0, 0, 18, 4);
        _colors.Click += (_, _) => ShowColors();
        bar.Children.Add(_colors);

        _level.FontFamily = (FontFamily)Res("FontMono"); _level.FontSize = 13; _level.Foreground = (Brush)Res("GoldBrush");
        _level.VerticalAlignment = VerticalAlignment.Center; _level.Margin = new Thickness(0, 0, 18, 4);
        bar.Children.Add(_level);
        _saveWav.VerticalAlignment = VerticalAlignment.Center; _saveWav.Foreground = (Brush)Res("DimBrush"); _saveWav.Margin = new Thickness(0, 0, 18, 4);
        bar.Children.Add(_saveWav);
        _shot.Style = (Style)Res("CdsButton"); _shot.Margin = new Thickness(0, 0, 8, 4); _shot.Click += (_, _) => ExportPng();
        _csv.Style = (Style)Res("CdsButton"); _csv.Margin = new Thickness(0, 0, 0, 4); _csv.Click += (_, _) => ExportCsv();
        bar.Children.Add(_shot); bar.Children.Add(_csv);
        Grid.SetRow(bar, 0); root.Children.Add(bar);

        var panel = new Border { Style = (Style)Res("CdsPanel"), Child = _chart, Margin = new Thickness(0) };
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        _status.FontSize = 12; _status.Foreground = (Brush)Res("GoldBrush"); _status.Margin = new Thickness(0, 8, 0, 0);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(_status, 2); root.Children.Add(_status);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        UpdateChart();
    }

    private void AddGroup(WrapPanel host, string labelKey, Action<object> on, params (string Key, object Val)[] items)
    {
        var tb = new TextBlock { Style = (Style)Res("DimLabel"), Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        _labels.Add((tb, labelKey)); host.Children.Add(tb);
        bool first = true;
        foreach (var (key, val) in items)
        {
            var rb = new RadioButton { Style = (Style)Res("CdsSeg"), GroupName = labelKey, Tag = val, IsChecked = first };
            rb.Margin = new Thickness(0, 0, 0, 4);
            rb.Checked += (s, _) => on(((RadioButton)s).Tag);
            _radios.Add((rb, key)); host.Children.Add(rb);
            first = false;
        }
        host.Children.Add(new Border { Width = 18 });
    }

    private void ApplyLang()
    {
        _start.Content = Loc.T(_running ? "wfs.stop" : "wfs.start");
        _clear.Content = Loc.T("rec.clear");
        _colors.Content = Loc.T("rec.colors");
        _all.IsEnabled = !_running;
        _all.Content = _seqWait && _seq.Count > 0 ? Loc.T("rec.next") + LvlText(_seq.Peek()) : Loc.T("rec.all");
        _saveWav.Content = Loc.T("pb.savewav");
        _shot.Content = Loc.T("wfs.shot");
        _csv.Content = Loc.T("wfs.csv");
        foreach (var (tb, key) in _labels) tb.Text = Loc.T(key);
        foreach (var (rb, key) in _radios) rb.Content = Loc.T(key);
        _chart.Refresh();
    }

    // ---------- zajem ----------
    private Action<float[], int, int> MakeSink(int rate, int ch)
    {
        _rate = rate; _chans = Math.Clamp(ch, 1, 2);
        lock (_lock) { foreach (var l in _chunks) l.Clear(); _peakL = _peakR = 0; }
        lock (_wavLock)
        {
            _wav = null; _wavPath = null;
            if (_saveWav.IsChecked == true)
            {
                try
                {
                    _wavPath = System.IO.Path.Combine(OutDir(), $"REC_capture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav");
                    _wav = new WaveFileWriter(_wavPath, WaveFormat.CreateIeeeFloatWaveFormat(rate, ch));
                }
                catch { _wav = null; _wavPath = null; }
            }
        }
        _liveRec?.Dispose();
        _live = new PbCore(rate, _chans);
        _liveRec = new PbRecorder(_live);
        var liveRec = _liveRec;
        int chans = _chans;
        return (buf, n, c) =>
        {
            liveRec.Feed(buf, n, c);
            for (int k = 0; k < chans && k < c; k++)
            {
                var a = new float[n];
                double pk = 0;
                for (int i = 0; i < n; i++) { float v = buf[i * c + k]; a[i] = v; double av = Math.Abs(v); if (av > pk) pk = av; }
                lock (_lock) { _chunks[k].Add(a); if (k == 0) _peakL = pk; else _peakR = pk; }
            }
            lock (_wavLock) { try { _wav?.WriteSamples(buf, 0, n * c); } catch { } }
        };
    }

    // ---------- zaporedje vseh 4 meritev ----------
    private readonly Queue<double> _seq = new();
    private bool _seqActive, _seqWait;

    private const int SeqPauseSec = 5;
    private int _seqLeft;
    private DispatcherTimer? _seqTimerField;
    private DispatcherTimer _seqTimer
    {
        get
        {
            if (_seqTimerField == null)
            {
                _seqTimerField = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _seqTimerField.Tick += (_, _) =>
                {
                    if (!_seqWait || !_seqActive) { _seqTimerField!.Stop(); return; }
                    if (--_seqLeft <= 0) { _seqTimerField!.Stop(); OnAll(); }
                    else _status.Text = Loc.T("rec.seqwait") + LvlText(_seq.Peek()) + $"  ({_seqLeft} s)";
                };
            }
            return _seqTimerField;
        }
    }

    private void CancelSeq() { _seqTimerField?.Stop(); _seq.Clear(); _seqActive = false; _seqWait = false; ApplyLang(); }

    private void SelectRadio(string group, double val)
    {
        foreach (var (rb, _) in _radios)
            if (rb.GroupName == group && rb.Tag is double d && d == val) rb.IsChecked = true;
    }

    private void OnAll()
    {
        if (_running) return;
        _seqTimerField?.Stop();
        if (!_seqActive)
        {
            _done.Clear(); _cols.Clear();
            _seq.Clear();
            foreach (var l in new[] { -20.0, -10.0, 0.0, 6.0 }) _seq.Enqueue(l);
            _seqActive = true;
            SelectRadio("rec.dur", 60.0);
        }
        _seqWait = false;
        _lvl = _seq.Dequeue();
        SelectRadio("rec.lvl", _lvl);
        UpdateChart();
        StartMeasure();
        if (!_running) CancelSeq();
    }

    private void StartMeasure()
    {
        if (_running) return;
        _asio ??= new AsioCapture();
        _driver = AsioCapture.FindDefault();
        if (_driver == null) { _status.Text = Loc.T("wfs.noasio"); return; }
        _label = LvlText(_lvl);
        _liveId = ++_nextId;
        _cols[_liveId] = DefaultCols(_done.Count(d => d.Lvl == _lvl));
        _gen = null;
        string? err = _asio.StartDuplex(_driver,
            rate =>
            {
                _f1 = Math.Min(F1Max, 0.45 * rate);
                _gen = new SweepGen(rate, F0, _f1, _dur, _lvl + OutRefDbfs);
                return _gen;
            },
            MakeSink);
        if (err != null) { _status.Text = Loc.T("wfs.asioerr") + err; return; }
        _source = $"Recording · ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
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
        // konec: sweep je odvrtel + nekaj sekund za zakasnitev kasetofona
        UpdateChart();
        if (_gen != null && _gen.Finished && t > _gen.Seconds + 3) Finish(true);
    }

    // Odstrani točke, ki skočijo nazaj proti nizkim frekvencam (ni del sweepa), ostale uredi po frekvenci.
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

    private static float[] Concat(List<float[]> parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        var o = new float[total];
        int pos = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, pos * sizeof(float), p.Length * sizeof(float)); pos += p.Length; }
        return o;
    }

    private void Finish(bool auto)
    {
        if (!_running) return;
        _timer.Stop(); _sw.Stop();
        try { _asio?.Stop(); } catch { }
        lock (_wavLock) { try { _wav?.Dispose(); } catch { } _wav = null; }
        _running = false; _level.Text = "";
        try { _liveRec?.Stop(); } catch { }          // izprazni vrsto in obdela zadnja okna
        ApplyLang();

        // Končna krivulja = izmerjene točke tona (enako kot v živo). Koherentna analiza znanega sweepa tu ne deluje,
        // ker zakasnitev med snemalno in predvajalno glavo niha z neenakomernostjo traku.
        var live = _live;
        string wavNote = _wavPath != null ? "  WAV: " + _wavPath : "";
        _wavPath = null;
        if (live == null || live.PointCount < 10) { CancelSeq(); _status.Text = Loc.T("pb.noresp") + wavNote; return; }
        var p0 = Clean(live.Points(0));
        var p1 = live.Channels >= 2 ? Clean(live.Points(1)) : null;
        _done.Add((_label, _lvl, _liveId,
                   p0.Select(p => p.F).ToArray(), p0.Select(p => p.Db).ToArray(),
                   p1?.Select(p => p.F).ToArray(), p1?.Select(p => p.Db).ToArray()));
        var sorted = _done.OrderBy(x => x.Lvl).ThenBy(x => x.Id).ToList();
        _done.Clear(); _done.AddRange(sorted);
        _live = null;
        UpdateChart();
        _status.Text = (auto ? Loc.T("rec.done") : Loc.T("rec.stopped")) + $" ({_label})" + wavNote;
        if (!auto) CancelSeq();
        if (auto && _seqActive && _seq.Count > 0)
        {
            _seqWait = true;                       // kratek premor, nato samodejno naslednja meritev
            _seqLeft = SeqPauseSec;
            _status.Text = Loc.T("rec.seqwait") + LvlText(_seq.Peek()) + $"  ({_seqLeft} s)";
            _seqTimer.Stop(); _seqTimer.Start();
            ApplyLang();
        }
        else
        {
            CancelSeq();
            if (auto) ExportPng();            // samodejno shrani sliko grafa (le z licenco)
        }
    }

    // ---------- prikaz ----------
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

    // ---------- barve krivulj (paleta kot pri Yoshimasi) ----------
    private readonly Dictionary<int, (Color L, Color R)> _cols = new();
    private int _nextId, _liveId;
    private static (Color L, Color R) DefaultCols(int k) => (k % 3) switch
    {
        0 => (Color.FromRgb(0x4D, 0xA3, 0xFF), Color.FromRgb(0xE3, 0x00, 0x1B)),
        1 => (Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF)),
        _ => (Color.FromRgb(0x80, 0xFF, 0x00), Color.FromRgb(0xFF, 0x80, 0x00)),
    };
    private (Color L, Color R) Cols(int id) =>
        _cols.TryGetValue(id, out var c) ? c : DefaultCols(0);

    private static readonly string[] Palette =
    {
        "FF8080","FFFF80","80FF80","00FF80","80FFFF","0080FF","FF80C0","FF80FF",
        "FF0000","FFFF00","80FF00","00FF40","00FFFF","0080C0","8080C0","FF00FF",
        "804040","FF8040","00FF00","008080","004080","8080FF","800040","FF0080",
        "800000","FF8000","008000","008040","0000FF","0000A0","800080","8000FF",
        "400000","804000","004000","004040","000080","000040","400040","400080",
        "000000","808000","808040","808080","408080","C0C0C0","602060","FFFFFF",
    };

    private void ShowColors()
    {
        if (_done.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
        var win = new Window
        {
            Title = Loc.T("rec.colors"), SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            Background = (Brush)Res("PanelBrush"),
        };
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = Loc.T("rec.colors.hint"), Foreground = (Brush)Res("DimBrush"), Margin = new Thickness(0, 0, 0, 12), MaxWidth = 380, TextWrapping = TextWrapping.Wrap });

        Button? sel = null; int selLvl = 0; bool selIsL = true;
        void Mark(Button b, bool on) { b.BorderBrush = on ? Brushes.White : (Brush)Res("DimBrush"); b.BorderThickness = new Thickness(on ? 3 : 1); }
        Button Swatch(int lvl, bool isL)
        {
            var c = isL ? Cols(lvl).L : Cols(lvl).R;
            var b = new Button { Width = 70, Height = 26, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(c), Content = isL ? "L" : "R", Foreground = Brushes.Black };
            Mark(b, false);
            b.Click += (_, _) => { if (sel != null) Mark(sel, false); sel = b; selLvl = lvl; selIsL = isL; Mark(b, true); };
            return b;
        }
        foreach (var d in _done)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = d.Label, Width = 70, Foreground = (Brush)Res("GoldBrush"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Swatch(d.Id, true));
            if (d.R != null) row.Children.Add(Swatch(d.Id, false));
            stack.Children.Add(row);
        }

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8, Margin = new Thickness(0, 10, 0, 12) };
        foreach (var hex in Palette)
        {
            var col = (Color)ColorConverter.ConvertFromString("#" + hex);
            var pb = new Button { Width = 32, Height = 26, Margin = new Thickness(2), Background = new SolidColorBrush(col), BorderBrush = (Brush)Res("DimBrush") };
            pb.Click += (_, _) =>
            {
                if (sel == null) return;
                var cur = Cols(selLvl);
                _cols[selLvl] = selIsL ? (col, cur.R) : (cur.L, col);
                sel.Background = new SolidColorBrush(col);
                UpdateChart();
            };
            grid.Children.Add(pb);
        }
        stack.Children.Add(grid);
        var ok = new Button { Content = "OK", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => win.Close();
        stack.Children.Add(ok);
        win.Content = stack;
        win.ShowDialog();
    }

    private bool _autoRange;
    private void UpdateChart()
    {
        _chart.SetSeries(BuildSeries(), _autoRange);
    }

    // ---------- izvoz (le z licenco) ----------
    private static string OutDir() => AppSettings.OutDir();

    private void ExportPng()
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
        if (_done.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
        const int pw = 1600, ph = 800;
        var ch = new FrChart { Width = pw, Height = ph, Caption = $"{_source}, {DateTime.Now:dd.MM.yyyy}" };
        ch.SetSeries(BuildSeries(), _autoRange);
        ch.Measure(new Size(pw, ph)); ch.Arrange(new Rect(0, 0, pw, ph)); ch.UpdateLayout();
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(ch);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        string file = OutName.Next(OutDir(), "REC.Freq.Resp", ".png");
        using (var fs = System.IO.File.Create(file)) enc.Save(fs);
        _status.Text = Loc.T("wfs.saved") + file;
    }

    private void ExportCsv()
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
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
        string file = OutName.Next(OutDir(), "REC.Freq.Resp", ".csv", true);
        System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        _status.Text = Loc.T("wfs.saved") + file;
    }

    /// <summary>Povpreči izmerjene točke na mrežo 1/24 oktave (za CSV).</summary>
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
        try
        {
            if (_done.Count > 0)
                SessionReport.Rec = new RecSnapshot { Series = BuildSeries(), Auto = _autoRange, Source = _source };
        }
        catch { }
        Loc.Changed -= ApplyLang;
        _timer.Stop();
        try { _asio?.Stop(); } catch { }
        lock (_wavLock) { try { _wav?.Dispose(); } catch { } _wav = null; }
    }
}
