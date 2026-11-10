using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CDSMeasure.Controls;
using CDSMeasure.Core;
using Microsoft.Win32;
using NAudio.Wave;

namespace CDSMeasure.Views;

/// <summary>Playback frekvenčni odziv: sweep s kasete → nivo v pasovih 1/3 oktave (L modro, D rdeče).</summary>
public sealed class PbView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AsioCapture? _asio;
    private PbRecorder? _rec;
    private PbCore? _core;
    private string? _asioDriver;
    private bool _running, _busy, _stepsMode, _norm, _peak = true;
    private double _span = 50;
    private string _source = "";
    private double _lastGrow;
    private double _offs, _allL = double.NaN, _allR = double.NaN;     // zadržan maksimum čez spekter (L/D)
    private bool _allNorm, _allPeak;
    private int _liveMax = -1, _backCount;
    private double _liveL = double.NaN, _liveR = double.NaN;
    private int _lastCount;
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private readonly Button _start = new(), _import = new(), _shot = new(), _csv = new();
    private readonly CheckBox _saveWav = new();
    private WaveFileWriter? _wav;
    private readonly object _wavLock = new();
    private string? _wavPath;
    private readonly TextBlock _elapsed = new(), _level = new(), _status = new();
    private readonly BandChart _chart = new();
    private readonly List<(RadioButton Rb, string Key)> _radios = new();
    private readonly List<(TextBlock Tb, string Key)> _labels = new();

    private static object Res(string key) => Application.Current.Resources[key];

    public PbView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ena sama vrstica ukazov (zaseda čim manj prostora grafu)
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.Style = (Style)Res("CdsPrimary"); _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 14, 4);
        _start.Click += (_, _) => { if (_running) StopMeasure(true); else StartMeasure(); };
        _elapsed.FontFamily = (FontFamily)Res("FontMono"); _elapsed.FontSize = 20; _elapsed.Foreground = (Brush)Res("TextBrush");
        _elapsed.VerticalAlignment = VerticalAlignment.Center; _elapsed.Margin = new Thickness(0, 0, 14, 4); _elapsed.Text = "00:00";
        _import.Style = (Style)Res("CdsButton"); _import.Margin = new Thickness(0, 0, 18, 4); _import.Click += (_, _) => ImportFile();
        bar.Children.Add(_start); bar.Children.Add(_elapsed);
        bar.Children.Add(_import);

        AddGroup(bar, "pb.mode", v => { _stepsMode = (bool)v; }, ("pb.sweep", false), ("pb.steps", true));
        AddGroup(bar, "pb.calc", v => { _peak = (bool)v; UpdateChart(); }, ("pb.peak", true), ("pb.mean", false));
        AddGroup(bar, "pb.ref", v => { _norm = (bool)v; UpdateChart(); }, ("pb.abs", false), ("pb.norm", true));
        AddGroup(bar, "pb.range", v => { _span = (double)v; UpdateChart(); }, ("+10 … −40 dB", 50.0), ("20 dB", 20.0), ("40 dB", 40.0), ("Auto", 0.0));

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

    // ---------- pomožne za gradnjo ----------
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
        _import.Content = Loc.T("pb.import");
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
        _core = new PbCore(rate, ch) { StepsMode = _stepsMode };
        _rec = new PbRecorder(_core);
        lock (_wavLock)
        {
            _wav = null; _wavPath = null;
            if (_saveWav.IsChecked == true)
            {
                try
                {
                    _wavPath = System.IO.Path.Combine(OutDir(), $"PB_capture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav");
                    _wav = new WaveFileWriter(_wavPath, WaveFormat.CreateIeeeFloatWaveFormat(rate, ch));
                }
                catch { _wav = null; _wavPath = null; }
            }
        }
        var rec = _rec;
        return (buf, n, c) =>
        {
            rec.Feed(buf, n, c);
            lock (_wavLock) { try { _wav?.WriteSamples(buf, 0, n * c); } catch { } }
        };
    }

    private void StartMeasure()
    {
        if (_busy) return;
        _asio ??= new AsioCapture();
        _asioDriver = AsioCapture.FindDefault();
        if (_asioDriver == null) { _status.Text = Loc.T("wfs.noasio"); return; }
        _rec?.Dispose(); _rec = null; _core = null;
        string? err = _asio.StartRaw(_asioDriver, MakeSink);
        if (err != null) { _core = null; _status.Text = Loc.T("wfs.asioerr") + err; return; }
        _source = $"ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
        _lastCount = 0; _lastGrow = 0; _allL = _allR = double.NaN; _liveMax = -1; _backCount = 0;
        _sw.Restart(); _timer.Start(); _running = true;
        _status.Text = Loc.T("pb.waiting");
        ApplyLang();
        UpdateChart();
    }

    private void StopMeasure(bool manual)
    {
        _asio?.Stop();
        lock (_wavLock) { try { _wav?.Dispose(); } catch { } _wav = null; }
        _rec?.Stop();                       // izprazni vrsto in obdela zadnja okna
        _timer.Stop(); _sw.Stop();
        _running = false;
        _level.Text = "";
        UpdateChart();
        int n = _core?.PointCount ?? 0;
        _status.Text = (n >= 10 ? Loc.T("pb.done") : Loc.T("pb.noresp")) + (_wavPath != null ? "  WAV: " + _wavPath : "");
        _wavPath = null;
        ApplyLang();
        // samodejni konec (ni več signala): shrani sliko grafa
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
        // konec sweepa: nekaj sekund brez novih točk po vsaj 30 veljavnih oknih
        if (count >= 30 && t - _lastGrow >= 8) StopMeasure(false);
    }

    // ---------- uvoz datoteke ----------
    private async void ImportFile()
    {
        if (_busy || _running) return;
        var dlg = new OpenFileDialog { Filter = "Audio (*.wav;*.flac;*.mp3)|*.wav;*.flac;*.mp3|All|*.*" };
        if (dlg.ShowDialog() != true) return;
        _busy = true; _status.Text = Loc.T("pb.analysing");
        string path = dlg.FileName; bool steps = _stepsMode;
        try
        {
            var core = await Task.Run(() => PbFile.Analyze(path, steps));
            _core = core; _allL = _allR = double.NaN;
            _source = System.IO.Path.GetFileName(path);
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
        var raw = new List<(string Label, FrPoint[] P0, FrPoint[]? P1)>();
        var core = _core;
        if (core != null && core.PointCount > 0)
            raw.Add(("", core.Points(0), core.Channels >= 2 ? core.Points(1) : null));

        var list = new List<BandChart.Series>();
        double mx = double.NegativeInfinity, mn = double.PositiveInfinity;
        foreach (var (label, p0, p1) in raw)
        {
            double[] l = band(p0);
            double[]? r = p1 != null ? band(p1) : null;
            _offs = 0;
            if (_norm)
            {
                // referenca pri 1 kHz (povprečje kanalov) za vsako meritev posebej
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

    /// <summary>Aktivni pas = pas zadnje izmerjene točke (le med merjenjem); stolpec kaže njen trenutni nivo.</summary>
    private void SetLive()
    {
        var core = _core;
        double t = _sw.Elapsed.TotalSeconds;
        // stolpec le med merjenjem in le, dokler prihajajo nove točke (sicer se zloži v črtico)
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
        // sweep gre le navzgor: pas ne skače nazaj (ni utripanja, končni 1 kHz ton ga ne sproži znova)
        // večji skok nazaj (npr. začetek sweepa po kalibracijskem tonu 1 kHz) sprejmemo šele, ko traja ~1 s
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

    // ---------- izvoz (le z licenco) ----------
    private static string OutDir() => AppSettings.OutDir();

    private void ExportPng()
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
        var (s, top, span, yt) = Build();
        if (s.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
        const int pw = 1600, ph = 800;
        var ch = new BandChart { Width = pw, Height = ph, YTitle = yt, ShowRight = _chart.ShowRight,
                                 Caption = $"{_source}, {DateTime.Now:dd.MM.yyyy}" };
        ch.MaxL = _allL; ch.MaxR = _allR;
        ch.SetSeries(s, top, span);
        ch.Measure(new Size(pw, ph)); ch.Arrange(new Rect(0, 0, pw, ph)); ch.UpdateLayout();
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(ch);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        string file = OutName.Next(OutDir(), "PB.Freq.Resp", ".png");
        using (var fs = System.IO.File.Create(file)) enc.Save(fs);
        _status.Text = Loc.T("wfs.saved") + file;
    }

    private void ExportCsv()
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
        var (s, _, _, _) = Build();
        if (s.Count == 0) { _status.Text = Loc.T("pb.noresp"); return; }
        string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
        var head = new List<string> { "band_hz" };
        foreach (var x in s)
        {
            string sfx = x.Label.Length > 0 ? "_" + x.Label.Replace(" ", "") : "";
            head.Add("left" + sfx); if (x.R != null) head.Add("right" + sfx);
        }
        var sb = new StringBuilder(string.Join(';', head) + "\n");
        for (int i = 0; i < PbCore.Centers.Length; i++)
        {
            var row = new List<string> { PbCore.Labels[i] };
            foreach (var x in s) { row.Add(F(x.L![i])); if (x.R != null) row.Add(F(x.R[i])); }
            sb.AppendLine(string.Join(';', row));
        }
        string file = OutName.Next(OutDir(), "PB.Freq.Resp", ".csv", true);
        System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        _status.Text = Loc.T("wfs.saved") + file;
    }

    public void Dispose()
    {
        try
        {
            if ((_core?.PointCount ?? 0) >= 10)
            {
                var (s, top, span, yt) = Build();
                if (s.Count > 0)
                    SessionReport.Pb = new PbSnapshot { Series = s, Top = top, Span = span, YTitle = yt, ShowRight = _chart.ShowRight, Source = _source };
            }
        }
        catch { }
        Loc.Changed -= ApplyLang;
        _timer.Stop();
        try { _asio?.Stop(); } catch { }
        lock (_wavLock) { try { _wav?.Dispose(); } catch { } _wav = null; }
        _rec?.Dispose();
    }
}
