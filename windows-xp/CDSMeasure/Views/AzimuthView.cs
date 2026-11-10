using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CDSMeasure.Controls;
using CDSMeasure.Core;

namespace CDSMeasure.Views;

/// <summary>Azimut glave (predvajanje): fazna razlika L − D testnega tona 10 / 12,5 / 15 kHz v živo.</summary>
public sealed class AzimuthView : UserControl, IDisposable
{
    private double _zoneUs = 2.0;       // ciljno območje (±2 / ±5 / ±10 µs)

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private AsioCapture? _asio;
    private AzimuthCore? _core;
    private bool _running;
    private double _range = 90, _nominal;
    private double _maxSum = double.NegativeInfinity;
    private readonly System.Diagnostics.Stopwatch _sw = new();

    private readonly Button _start = new(), _shot = new(), _resetMax = new(), _recVid = new();
    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly List<byte[]> _frames = new();
    private bool _recMp4; private int _fw, _fh;
    private readonly List<Point> _hist = new();
    private AzimuthResult? _last;
    private string _source = "";
    private DateTime _recStart; private bool _recording;
    private const double RecSeconds = 30, RecWidth = 760;
    private readonly TextBlock _phase = new(), _delay = new(), _tone = new(), _levels = new(), _warn = new(), _status = new(), _hint = new(), _zoneNote = new();
    private readonly AzimuthGauge _gauge = new();
    private readonly PhaseTrace _trace = new();
    private readonly LissajousScope _liss = new();
    private readonly Grid _scopeHost = new();
    private readonly float[] _lsL = new float[1024], _lsR = new float[1024];
    private bool _showLiss;
    private readonly List<(RadioButton Rb, string Key)> _radios = new();
    private readonly List<(TextBlock Tb, string Key)> _labels = new();

    private static object Res(string key) => Application.Current.Resources[key];

    public AzimuthView()
    {
        var root = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start.Style = (Style)Res("CdsPrimary"); _start.MinWidth = 120; _start.Margin = new Thickness(0, 0, 18, 4);
        _start.Click += (_, _) => { if (_running) StopMeasure(); else StartMeasure(); };
        bar.Children.Add(_start);
        AddGroup(bar, "az.freq", v => { _nominal = (double)v; if (_core != null) _core.NominalHz = _nominal; },
            ("az.auto", 0.0), ("10 kHz", 10000.0), ("12,5 kHz", 12500.0), ("15 kHz", 15000.0));
        AddGroup(bar, "az.range", v => { _range = (double)v; RefreshStatic(); },
            ("±30°", 30.0), ("±90°", 90.0), ("±180°", 180.0));
        AddGroup(bar, "az.zone", v => { _zoneUs = (double)v; RefreshStatic(); },
            ("±2 µs", 2.0), ("±5 µs", 5.0), ("±10 µs", 10.0));
        AddGroup(bar, "az.view", v => { _showLiss = (bool)v; _liss.Visibility = _showLiss ? Visibility.Visible : Visibility.Collapsed; if (!_showLiss) _liss.Clear(); },
            ("az.v.trace", false), ("az.v.liss", true));
        _resetMax.Style = (Style)Res("CdsButton"); _resetMax.Margin = new Thickness(0, 0, 8, 4);
        _resetMax.Click += (_, _) => { _maxSum = double.NegativeInfinity; };
        _shot.Style = (Style)Res("CdsButton"); _shot.Margin = new Thickness(0, 0, 0, 4);
        _shot.Click += (_, _) => ExportPng();
        _recVid.Style = (Style)Res("CdsButton"); _recVid.Margin = new Thickness(8, 0, 0, 4);
        _recVid.Click += (_, _) => { if (_recording) StopRecording(); else StartRecording(true); };
        _recVid.Visibility = Visibility.Collapsed;      // XP: brez videa
        _recTimer.Tick += (_, _) => RecTick();
        bar.Children.Add(_resetMax); bar.Children.Add(_shot); bar.Children.Add(_recVid);
        Grid.SetRow(bar, 0); root.Children.Add(bar);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var read = new Grid { Margin = new Thickness(18, 14, 18, 6) };
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        read.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Big(_phase, 56, "GoldLightBrush"); Big(_delay, 40, "TextBrush"); Big(_tone, 28, "DimBrush");
        _tone.VerticalAlignment = VerticalAlignment.Center; _tone.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_phase, 0); Grid.SetColumn(_delay, 1); Grid.SetColumn(_tone, 2);
        read.Children.Add(_phase); read.Children.Add(_delay); read.Children.Add(_tone);
        Grid.SetRow(read, 0); body.Children.Add(read);

        Grid.SetRow(_gauge, 1); body.Children.Add(_gauge);
        _scopeHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _scopeHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _trace.Margin = new Thickness(0, 6, 0, 0);
        _liss.Margin = new Thickness(12, 6, 0, 0); _liss.Visibility = Visibility.Collapsed;
        _scopeHost.SizeChanged += (_, e) => _liss.Width = Math.Max(0, e.NewSize.Height - 6);   // kvadratno polje
        Grid.SetColumn(_trace, 0); Grid.SetColumn(_liss, 1);
        _scopeHost.Children.Add(_trace); _scopeHost.Children.Add(_liss);
        Grid.SetRow(_scopeHost, 2); body.Children.Add(_scopeHost);

        var low = new StackPanel { Margin = new Thickness(18, 8, 18, 12) };
        _levels.FontFamily = (FontFamily)Res("FontMono"); _levels.FontSize = 16; _levels.Foreground = (Brush)Res("TextBrush");
        _warn.FontSize = 13; _warn.Foreground = (Brush)Res("RedBrush"); _warn.Margin = new Thickness(0, 4, 0, 0);
        _hint.FontSize = 12; _hint.Foreground = (Brush)Res("DimBrush"); _hint.TextWrapping = TextWrapping.Wrap; _hint.Margin = new Thickness(0, 6, 0, 0);
        _zoneNote.FontSize = 12; _zoneNote.Foreground = (Brush)Res("DimBrush");
        low.Children.Add(_levels); low.Children.Add(_warn); low.Children.Add(_zoneNote); low.Children.Add(_hint);
        Grid.SetRow(low, 3); body.Children.Add(low);

        var panel = new Border { Style = (Style)Res("CdsPanel"), Child = body, Margin = new Thickness(0) };
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        _status.FontSize = 12; _status.Foreground = (Brush)Res("GoldBrush"); _status.Margin = new Thickness(0, 8, 0, 0);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(_status, 2); root.Children.Add(_status);

        Content = root;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        RefreshStatic();
        ShowEmpty();
    }

    private static void Big(TextBlock tb, double size, string brushKey)
    {
        tb.FontFamily = (FontFamily)Res("FontMono"); tb.FontSize = size; tb.Foreground = (Brush)Res(brushKey);
        tb.VerticalAlignment = VerticalAlignment.Center;
    }

    private void AddGroup(WrapPanel host, string labelKey, Action<object> on, params (string Key, object Val)[] items)
    {
        var tb = new TextBlock { Style = (Style)Res("DimLabel"), Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        _labels.Add((tb, labelKey)); host.Children.Add(tb);
        bool first = true;
        foreach (var (key, val) in items)
        {
            var rb = new RadioButton { Style = (Style)Res("CdsSeg"), GroupName = labelKey, Tag = val, IsChecked = key == "±90°" || (labelKey != "az.range" && first) };
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
        _shot.Content = Loc.T("wfs.shot");
        if (!_recording) { _recVid.Content = Loc.T("az.recmp4"); }
        _resetMax.Content = Loc.T("az.resetmax");
        foreach (var (tb, key) in _labels) tb.Text = Loc.T(key);
        foreach (var (rb, key) in _radios) rb.Content = key.StartsWith("az.") ? Loc.T(key) : key;
        _hint.Text = Loc.T("az.hint");
        if (!_running) _status.Text = Loc.T("az.idle");
    }

    private double ZoneDeg(double freqHz) => _zoneUs * 1e-6 * 360.0 * (freqHz > 0 ? freqHz : (_nominal > 0 ? _nominal : 10000));

    private void RefreshStatic()
    {
        double f = _core?.Latest.FreqHz ?? 0;
        _gauge.Set(0, false, _range, ZoneDeg(f));
        _trace.Set(_range, ZoneDeg(f), _sw.Elapsed.TotalSeconds);
    }

    private void ShowEmpty()
    {
        _phase.Text = "—"; _delay.Text = ""; _tone.Text = ""; _levels.Text = ""; _warn.Text = "";
    }

    // ---------- zajem ----------
    private Action<float[], int, int> MakeSink(int rate, int ch)
    {
        var core = new AzimuthCore(rate, ch) { NominalHz = _nominal };
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
        if (_asio.InputChannels < 2)
        {
            _asio.Stop(); _core = null;
            _status.Text = Loc.T("az.nostereo");
            return;
        }
        _maxSum = double.NegativeInfinity;
        _trace.Clear();
        _sw.Restart(); _timer.Start(); _running = true;
        _source = $"ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
        _hist.Clear(); _last = null;
        _status.Text = Loc.T("az.waiting") + $"   ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
        ApplyLang();
    }

    private void StopMeasure()
    {
        SaveToReport();
        _asio?.Stop();
        _timer.Stop(); _sw.Stop();
        _running = false;
        _core = null;
        _liss.Clear();
        _gauge.Set(0, false, _range, ZoneDeg(0));
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
        if (r.Valid) { _trace.Add(t, r.PhaseDeg); _last = r; _hist.Add(new Point(t, r.PhaseDeg)); if (_hist.Count > 700) _hist.RemoveRange(0, _hist.Count - 600); }
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

    // ---------- izvoz slike ----------
    private void ExportPng()
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
        var dpi = Dpi.Get(this);
        int pw = (int)(ActualWidth * dpi.DpiScaleX), ph = (int)(ActualHeight * dpi.DpiScaleY);
        if (pw < 10 || ph < 10) return;
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        rtb.Render(dv);
        rtb.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        string file = OutName.Next(AppSettings.OutDir(), "AZ Screen", ".png");
        using (var fs = System.IO.File.Create(file)) enc.Save(fs);
        _status.Text = Loc.T("wfs.saved") + file;
    }

    // ---------- 30 s snemanje okna (MP4 ali GIF) ----------
    private void StartRecording(bool mp4)
    {
        if (!License.IsLicensed) { _status.Text = Loc.T("lic.limit"); return; }
        if (ActualWidth < 50 || ActualHeight < 50) return;
        _frames.Clear();
        _recMp4 = mp4;
        _fw = (int)RecWidth & ~1; _fh = (int)Math.Round(ActualHeight * _fw / ActualWidth) & ~1;
        _recStart = DateTime.Now; _recording = true;
        _recTimer.Start();
        RecTick();
    }

    private byte[] Grab()
    {
        double s = (double)_fw / ActualWidth, sy = (double)_fh / ActualHeight;
        var rtb = new RenderTargetBitmap(_fw, _fh, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(s, sy));
            var r = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)), null, r);
            dc.DrawRectangle(new VisualBrush(this), null, r);
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private void RecTick()
    {
        double el = (DateTime.Now - _recStart).TotalSeconds;
        if (el >= RecSeconds) { StopRecording(); return; }
        _frames.Add(Grab());
        string t = Loc.T("az.recstop") + $"  {Math.Ceiling(RecSeconds - el):0} s";
        _recVid.Content = t;
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false; _recTimer.Stop();
        _recVid.Content = Loc.T("az.recmp4");
        var frames = _frames.ToList(); _frames.Clear();
        if (frames.Count < 2) return;
        bool mp4 = _recMp4; int w = _fw, h = _fh;
        int fps = (int)Math.Round(frames.Count / Math.Max(1.0, Math.Min(RecSeconds, (DateTime.Now - _recStart).TotalSeconds)));
        fps = MathX.Clamp(fps, 5, 30);
        string file = OutName.Next(AppSettings.OutDir(), "AZ Video", ".mp4");
        _status.Text = Loc.T("az.recenc");
        _recVid.IsEnabled = false;
        // XP izdaja: snemanje videa (MP4) ni na voljo (potrebuje Windows 10); gumb je skrit
        _status.Text = "";
        _recVid.IsEnabled = true;
    }

    private static BitmapSource Decode(byte[] png)
    {
        var dec = new PngBitmapDecoder(new System.IO.MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var f = dec.Frames[0]; f.Freeze(); return f;
    }

    private void SaveToReport()
    {
        try
        {
            if (_last is { Valid: true } r && _hist.Count >= 10)
            {
                double now = _hist[_hist.Count - 1].X;
                SessionReport.Az = new AzSnapshot
                {
                    FreqHz = r.FreqHz, PhaseDeg = r.PhaseDeg, DelayUs = r.DelayUs, LevelL = r.LevelLDb, LevelR = r.LevelRDb,
                    ZoneUs = _zoneUs, RangeDeg = _range, Source = _source,
                    Trace = _hist.Where(p => p.X >= now - 30).ToList(),
                };
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_running) SaveToReport();
        _recTimer.Stop(); _recording = false; _frames.Clear();
        Loc.Changed -= ApplyLang;
        _timer.Stop();
        try { _asio?.Stop(); } catch { }
        _core = null;
    }
}
