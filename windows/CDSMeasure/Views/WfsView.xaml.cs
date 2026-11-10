using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CDSMeasure.Controls;
using CDSMeasure.Core;
using System.Windows.Threading;

namespace CDSMeasure.Views;

/// <summary>
/// Zaslon W&amp;F&amp;S. Postavitev (zgoraj navzdol): 1. odstopanje hitrosti + digitalni prikaz,
/// 2. hitrost v časovni domeni, 3. wow &amp; flutter + digitalni prikaz, 4. wow &amp; flutter v frekvenčni domeni.
/// TRENUTNO: demo podatki. Prava meritev (ASIO + jedro iz prototipa) pride v naslednjem koraku.
/// </summary>
public partial class WfsView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _sw = new();
    private readonly Random _rnd = new(1);
    private readonly List<Point> _speed = new();   // (čas s, hitrost %)
    private readonly List<Point> _wf = new();      // (čas s, W&F utežen RMS %)
    private readonly List<Point> _pk = new();      // (čas s, W&F utežen vrh %)
    private readonly List<Point> _hz = new();      // (čas s, hitrost Hz)

    private bool _ready, _running, _showReport, _fixedAxes = true;
    private string _source = Loc.T("wfs.sim");
    private double _noSigT = -1;                 // čas, od katerega ni signala (−1 = signal je prisoten)
    private const int MinValidSec = 30;          // najmanj toliko sekund veljavne meritve, preden se vklopi samodejna ustavitev
    private const double NoSignalStopSec = 10;   // toliko sekund brez tona = konec kasete
    private double _refHz = 3000, _wfRange = 0.15, _window = 10, _durationMin = 45;
    private int _mode;                              // 0 utežen RMS, 1 utežen vrh, 2 neutežen
    private double _speedShown, _wfShown;
    private int _ticks;
    private AsioCapture? _asio;                       // živi zajem (ASIO)
    private WfCore? _core;
    private bool _useLive;
    private int _chan;
    private string? _asioDriver;
    private double _specWin = 16;                      // dolžina okna spektra [s]; ločljivost = 1 / okno
    private const double DemoFs = WfCore.OutFs;        // vzorčenje signala odstopanja hitrosti (demo in živo): 400 Hz
    private readonly List<float> _raw = new();          // odstopanje hitrosti [%] pri DemoFs
    private double _walk;
    private double[]? _specAvg; private int _specKey;                               // počasno naključno premikanje hitrosti
    private ModelSpec? _model;                      // izbran model (tovarniški podatki)
    private ModelSpec? _defModel;                   // privzeti maksimum mehanizma, dokler model ni izbran (najpogostejši)
    private bool _over;
    private MechProfile _mech = MechProfile.Generic;   // izbira mehanike je neobvezna: Generic = brez oznak

    public WfsView()
    {
        InitializeComponent();
        BuildMechButtons();
        _asioDriver = AsioCapture.FindDefault();
        SrcLive.IsEnabled = _asioDriver != null;
        if (_asioDriver != null) SrcLive.IsChecked = true;
        DateBox.Text = DateTime.Now.ToString("dd.MM.yyyy");
        _ready = true;
        _timer.Tick += (_, _) => Tick();
        Loc.Changed += ApplyLang;
        ApplyLang();
        ApplyRange();
        RedrawCharts(0);
    }

    // ---------- jezik ----------
    private void ApplyLang()
    {
        BtnStart.Content = Loc.T(_running ? "wfs.stop" : "wfs.start");
        DurLabel.Text = Loc.T("wfs.duration") + " (min)";
        BtnShot.Content = Wrap(Loc.T("wfs.shot"), 74);
        BtnCsv.Content = Wrap(Loc.T("wfs.csv"), 56);
        SpeedTitle.Text = Loc.T("wfs.speed");
        RefLabel.Text = Loc.T("wfs.ref");
        TimeTitle.Text = Loc.T("wfs.time");
        WinLabel.Text = Loc.T("wfs.window");
        WinAll.Content = Loc.T("wfs.all");
        WfTitle.Text = Loc.T("wfs.wf");
        ModeLabel.Text = Loc.T("wfs.mode");
        ModeWrms.Content = Loc.T("wfs.wrms");
        ModeWpk.Content = Loc.T("wfs.wpk");
        ModeUnw.Content = Loc.T("wfs.unw");
        RangeLabel.Text = Loc.T("wfs.range");
        SpecTitle.Text = Loc.T("wfs.spec");
        MechLabel.Text = Loc.T("wfs.mech");
        SpecWinLabel.Text = Loc.T("wfs.specwin");
        foreach (var rb in MechPanel.Children.OfType<RadioButton>())
            if (rb.Tag is MechProfile mp && mp.Id == "none") rb.Content = Loc.T("wfs.mech.none");
        UpdateMarkers();
        DemoText.Text = Loc.T("wfs.demo");
        DemoText.Visibility = _useLive ? Visibility.Collapsed : Visibility.Visible;
        SrcLabel.Text = Loc.T("wfs.src");
        SrcLive.Content = Loc.T("wfs.src.live");
        SrcDemo.Content = Loc.T("wfs.src.demo");
        ChanLabel.Text = Loc.T("wfs.chan");
        ChanL.Content = "L";
        ChanR.Content = Loc.Lang == "sl" ? "D" : "R";
        ChanLR.Content = Loc.Lang == "sl" ? "L+D" : "L+R";
        BtnImport.Content = Wrap(Loc.T("wfs.import"), 74);
        ViewLive.Content = Loc.T("view.live");
        ViewReport.Content = Loc.T("view.report");
        RepTitle.Text = Loc.T("rep.title");
        RepDevLabel.Text = Loc.T("rep.device");
        RepDateLabel.Text = Loc.T("rep.date");
        RepAxLabel.Text = Loc.T("rep.axes");
        AxFixed.Content = Loc.T("rep.fixed");
        AxAuto.Content = Loc.T("rep.auto");
        StatSpeedT.Text = Loc.T("rep.speed");
        StatWrmsT.Text = Loc.T("rep.wrms");
        StatPeakT.Text = Loc.T("rep.peak");
        if (_showReport) RefreshReport();
        WfModeText.Text = _mode switch { 0 => Loc.T("wfs.wrms"), 1 => Loc.T("wfs.wpk"), _ => Loc.T("wfs.unw") };
    }

    /// <summary>Besedilo gumba, ki se po potrebi prelomi v dve vrstici (omejena širina).</summary>
    private static TextBlock Wrap(string text, double maxWidth) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = maxWidth };

    // ---------- mehanika (neobvezna) ----------
    private void BuildMechButtons()
    {
        var style = (Style)FindResource("BarSeg");
        string[] order = { "sankyo", "sankyobd", "sankyosd", "classic", "zx9", "dragon", "none" };
        foreach (var id in order)
        {
            var prof = MechProfile.All.First(m => m.Id == id);
            var rb = new RadioButton
            {
                Style = style, GroupName = "mech", Tag = prof,
                Content = id == "none" ? Loc.T("wfs.mech.none") : MechLabelText(id, prof),
                IsChecked = id == "none"
            };
            if (prof.Models.Length > 0)
            {
                rb.ToolTip = new TextBlock { Text = string.Join(", ", prof.Models), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };   // seznam modelov ob miški
                ToolTipService.SetInitialShowDelay(rb, 150);
                ToolTipService.SetShowDuration(rb, 20000);
            }
            rb.Checked += (_, _) => { _mech = prof; _model = null; BuildModelButtons(); if (_ready) { UpdateLimit(); UpdateMarkers(); } };
            MechPanel.Children.Add(rb);
        }
    }

    /// <summary>Napis na gumbu mehanizma v dveh vrsticah: »Nakamichi« + ime.</summary>
    private static TextBlock MechLabelText(string id, MechProfile prof)
    {
        if (id.StartsWith("sankyo"))      // »Sankyo« + DD / BD / SD v dveh vrsticah
            return new TextBlock { Text = "Sankyo\n" + prof.Title[^2..], TextAlignment = TextAlignment.Center };
        string name = id == "zx9" ? "ZX-9" : prof.Title.Replace("Nakamichi ", "");
        return new TextBlock { Text = "Nakamichi\n" + name, TextAlignment = TextAlignment.Center };
    }

    /// <summary>Izbira modela (neobvezna): po izbiri mehanizma se pokažejo modeli s tovarniškimi podatki.</summary>
    private void BuildModelButtons()
    {
        ModelPanel.Children.Clear();
        _defModel = null;
        var specs = Specs.For(_mech.Id).ToList();
        // če imajo vsi modeli mehanizma enak maksimum, izbira modela ni potrebna (npr. Classic, ZX-9, Dragon)
        if (specs.Count > 0 && specs.Select(q => q.WfMax).Distinct().Count() == 1) _model = specs[0];
        bool pick = specs.Count > 0 && _model == null;
        if (pick) _defModel = specs.GroupBy(q => q.WfMax).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().First();   // npr. Sankyo DD: 0,027 %
        ModelPanel.Visibility = pick ? Visibility.Visible : Visibility.Collapsed;
        if (!pick) return;
        ModelPanel.Children.Add(new TextBlock { Text = Loc.T("wfs.model"), Style = (Style)FindResource("DimLabel"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        var style = (Style)FindResource("BarSeg");
        foreach (var sp in specs)
        {
            var rb = new RadioButton { Style = style, GroupName = "model", Content = sp.Model, Tag = sp,
                ToolTip = $"max {sp.WfMax:0.000} % {sp.Standard}", Margin = new Thickness(0, 0, 4, 4) };
            rb.Checked += (_, _) => { _model = sp; UpdateLimit(); };
            ModelPanel.Children.Add(rb);
        }
    }

    /// <summary>Označba tovarniškega maksimuma na traku W&amp;F; velja samo za utežen RMS (JIS), ker je tabela v WRMS.</summary>
    private void UpdateLimit()
    {
        var m = _model ?? _defModel;
        bool on = m != null && _mode == 0 && m.Standard.StartsWith("WRMS", StringComparison.OrdinalIgnoreCase);
        WfMeter.Limit = on ? m!.WfMax : double.NaN;
        WfMeter.LimitLabel = on ? $"{Loc.T("wfs.limit")} {m!.WfMax:0.000}" : "";
        if (!on && _over) { _over = false; WfValue.ClearValue(TextBlock.ForegroundProperty); }
    }

    /// <summary>Navpične oznake pričakovanih frekvenc v spektru; brez profila ni oznak.</summary>
    private void UpdateMarkers()
    {
        if (SpecChart == null) return;
        bool sl = Loc.Lang == "sl";
        double ratio = _hz.Count > 0 ? _hz.Average(p => p.Y) / _refHz : 1.0;   // izmerjena hitrost / nazivna
        SpecChart.Markers = _mech.Id == "none"
            ? null
            : _mech.Expected(ratio).Where(e => e.Hz > 0)
                   .Select(e => (e.Hz, $"{e.Hz:0.00} Hz · {(sl ? e.Sl : e.En)}")).ToList();
        SpecChart.Refresh();
    }

    // ---------- nastavitve ----------
    private static double TagVal(object sender) =>
        double.Parse((string)((RadioButton)sender).Tag, CultureInfo.InvariantCulture);

    private void Ref_Checked(object sender, RoutedEventArgs e)
    {
        _refHz = TagVal(sender);
        if (_ready && _running && _core != null) StartCapture();   // nova referenca → ponoven zagon zajema
    }

    private void Src_Checked(object sender, RoutedEventArgs e)
    {
        _useLive = TagVal(sender) == 1;
        if (_ready) ApplyLang();
    }

    private void Chan_Checked(object sender, RoutedEventArgs e)
    {
        _chan = (int)TagVal(sender);
        if (_ready && _running && _core != null) StartCapture();
    }

    private string? StartCapture()
    {
        _asio ??= new AsioCapture();
        _asioDriver = AsioCapture.FindDefault();     // upošteva Nastavitve
        if (_asioDriver == null) return Loc.T("wfs.noasio");
        var err = _asio.Start(_asioDriver, _chan, _refHz, out _core);
        if (err == null) _source = $"ASIO · {_asio.DriverName} · {_asio.SampleRate / 1000} kHz";
        return err;
    }
    private void Dur_Checked(object sender, RoutedEventArgs e) { _durationMin = TagVal(sender); }

    private void Win_Checked(object sender, RoutedEventArgs e)
    {
        _window = TagVal(sender);
        if (_ready) RedrawCharts(_sw.Elapsed.TotalSeconds);
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        _mode = (int)TagVal(sender);
        if (_ready) { ApplyLang(); UpdateLimit(); }
    }

    private void SpecWin_Checked(object sender, RoutedEventArgs e)
    {
        _specWin = TagVal(sender);
        if (_ready) RedrawCharts(_sw.Elapsed.TotalSeconds);
    }

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        _wfRange = TagVal(sender);
        if (_ready) { ApplyRange(); RedrawCharts(_sw.Elapsed.TotalSeconds); }
    }

    private void ApplyRange()
    {
        WfMeter.Maximum = _wfRange;
        WfMeter.MajorStep = _wfRange / 5;
        WfMeter.LabelFormat = _wfRange >= 1 ? "0.0" : "0.00";
    }

    // ---------- start / stop ----------
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { Stop(); return; }
        _speed.Clear(); _wf.Clear(); _pk.Clear(); _hz.Clear(); _raw.Clear(); _walk = 0; _specAvg = null; _noSigT = -1; EndText.Text = ""; _source = Loc.T("wfs.sim");
        _speedShown = 0; _wfShown = 0; _ticks = 0;
        if (_useLive)
        {
            string? err = StartCapture();
            if (err != null) { _core = null; StatusText.Text = Loc.T("wfs.asioerr") + err; return; }
            StatusText.Text = _source;
        }
        else { _core = null; StatusText.Text = ""; }
        _sw.Restart();
        _timer.Start();
        _running = true;
        ApplyLang();
    }

    private void Stop()
    {
        _asio?.Stop(); _core = null;
        LevelText.Text = "";
        _timer.Stop(); _sw.Stop();
        _running = false;
        ApplyLang();
    }


    private double Gauss()
    {
        double u1 = 1.0 - _rnd.NextDouble(), u2 = _rnd.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// DEMO signal kot pri pravem kasetofonu: počasen premik (ogrevanje + naključni drift), utripanje reela,
    /// stalni vrhovi mehanike (pri izbranem profilu na njegovih frekvencah) in šum.
    /// Brez profila uporabi Classic frekvence, da so vrhovi vidni tudi tam.
    /// </summary>
    private static readonly double[] HfF = { 12.3, 19.0, 31.5, 38.0, 46.2, 63.0 };
    private static readonly double[] HfA = { 0.012, 0.020, 0.045, 0.030, 0.025, 0.020 };

    private (double speed, double wf) DemoStep(double t)
    {
        var prof = _mech.Id == "none" ? MechProfile.All.First(m => m.Id == "classic") : _mech;
        var comps = prof.Expected(1.0).Where(e => e.Hz > 0).Select(e => e.Hz).ToArray();
        double[] amp = { 0.020, 0.013, 0.026, 0.012, 0.010 };      // amplituda vrha [%]
        int target = (int)(t * DemoFs);

        for (int n = _raw.Count; n < target; n++)
        {
            double tt = n / DemoFs;
            _walk += 0.0009 * Gauss() - 0.0004 * _walk;
            double x = -0.12 + 0.35 * Math.Exp(-tt / 240.0) + _walk;
            x += 0.018 * Math.Sin(2 * Math.PI * 0.45 * tt + 0.7) * (0.6 + 0.4 * Math.Sin(2 * Math.PI * tt / 97.0));
            for (int i = 0; i < comps.Length; i++)
            {
                double f = comps[i] * (i == 1 ? 1.01 : 1.0);        // supply capstan: rahel zdrs
                x += amp[i % amp.Length] * Math.Sin(2 * Math.PI * f * tt + 1.3 * i)
                     * (1 + 0.25 * Math.Sin(2 * Math.PI * tt / (40 + 7 * i)));
            }
            for (int i = 0; i < HfF.Length; i++)
                x += HfA[i] * Math.Sin(2 * Math.PI * HfF[i] * tt + 2.1 * i) * (1 + 0.4 * Math.Sin(2 * Math.PI * tt / (23 + 5 * i)));
            x += 0.045 * Gauss();
            _raw.Add((float)x);
        }

        int n1 = _raw.Count, n0 = Math.Max(0, n1 - 20);
        double sp = 0; for (int i = n0; i < n1; i++) sp += _raw[i];
        sp = n1 > n0 ? sp / (n1 - n0) : 0;

        // W&F (demo): RMS 1 s signala, zglajenega na ~10 Hz (približek utežitve)
        int w0 = Math.Max(20, n1 - (int)DemoFs);
        var sm = new List<double>();
        for (int i = w0; i < n1; i++)
        {
            double a = 0; for (int k = i - 19; k <= i; k++) a += _raw[k];
            sm.Add(a / 20.0);
        }
        double m = sm.Count > 0 ? sm.Average() : 0;
        double wf = sm.Count > 0 ? Math.Sqrt(sm.Sum(q => (q - m) * (q - m)) / sm.Count) : 0;
        return (sp, wf);
    }

    // ---------- demo meritev ----------
    private void Tick()
    {
        double t = _sw.Elapsed.TotalSeconds;
        double speed, wf0, pk, wf;
        bool valid = true;
        if (_core != null)
        {
            _core.DrainRaw(_raw);
            var snap = _core.Snapshot;
            LevelText.Text = snap.Settling ? Loc.T("wfs.settling")
                           : snap.Valid ? $"{snap.LevelDb:0.0} dBFS" : Loc.T("wfs.nosig");
            valid = snap.Valid && !snap.Settling;
            speed = snap.SpeedPct; wf0 = snap.Wrms; pk = snap.Wpeak;
            wf = _mode == 0 ? snap.Wrms : _mode == 1 ? snap.Wpeak : snap.Urms;
        }
        else
        {
            (speed, wf0) = DemoStep(t);
            pk = wf0 * 1.75;
            wf = wf0;
            if (_mode == 1) wf *= 1.4; else if (_mode == 2) wf *= 1.8;
        }

        // konec kasete: 10 s brez testnega tona (po vsaj eni veljavni meritvi) → samodejna ustavitev in poročilo
        if (_core != null)
        {
            if (valid) _noSigT = -1;
            else if (_wf.Count >= MinValidSec * 10)    // ~10 točk/s: ustavitev šele po ≥ 30 s veljavne meritve
            {
                if (_noSigT < 0) _noSigT = t;
                else if (t - _noSigT >= NoSignalStopSec)
                {
                    Stop();
                    ViewReport.IsChecked = true;
                    RefreshReport();
                    EndText.Text = Loc.T("wfs.endtape").ToUpperInvariant();
                    return;
                }
            }
        }

        if (valid)
        {
            if (_ticks % 3 == 0)    // seznami za poročilo: ~10 točk/s
            {
                _speed.Add(new Point(t, speed));
                _hz.Add(new Point(t, _refHz * (1 + speed / 100)));
                _wf.Add(new Point(t, wf0));
                _pk.Add(new Point(t, pk));
            }

            // balistika kazalca (33 ms korak)
            _speedShown += (speed - _speedShown) * 0.09;
            _wfShown += (wf - _wfShown) * 0.054;
            SpeedMeter.Value = _speedShown;
            WfMeter.Value = _wfShown;
            SpeedHz.Text = $"{_refHz * (1 + _speedShown / 100):0.0}  Hz";
            SpeedPct.Text = $"{_speedShown:+0.00;-0.00;0.00}  %";
            WfValue.Text = $"{_wfShown:0.000}  %";
            bool over = !double.IsNaN(WfMeter.Limit) && _wfShown > WfMeter.Limit;
            if (over != _over)
            {
                _over = over;
                if (over) WfValue.Foreground = (Brush)FindResource("RedBrush"); else WfValue.ClearValue(TextBlock.ForegroundProperty);
            }
        }
        else
        {
            SpeedHz.Text = "----  Hz"; SpeedPct.Text = "----  %"; WfValue.Text = "-.---  %";
        }

        var el = TimeSpan.FromSeconds(t);
        ElapsedText.Text = $"{(int)el.TotalHours:00}:{el.Minutes:00}:{el.Seconds:00}";

        RedrawTime(t);                          // časovni graf teče gladko (30 slik/s)
        if (++_ticks % 15 == 0) RedrawSpectrum();   // spekter ~2× na sekundo
        if (t >= _durationMin * 60) Stop();
    }

    // ---------- grafi ----------
    private void RedrawCharts(double t) { RedrawTime(t); RedrawSpectrum(); }

    private void RedrawTime(double t)
    {
        // 2. hitrost v času (drseče okno ali vse)
        double w = _window > 0 ? _window : Math.Max(10, t);
        double x1 = Math.Max(t, w), x0 = _window > 0 ? x1 - w : 0;
        if (_window == 0) x1 = Math.Max(10, t);

        var view = new List<Point>();
        double maxAbs = 0.1;
        if (_window > 0 && _window <= 60 && _raw.Count > 0)
        {
            // hitri vzorci (200 Hz), kot pri T100: odstopanje okoli povprečja okna
            int i1 = _raw.Count, i0 = Math.Max(0, i1 - (int)(_window * DemoFs));
            double mean = 0; for (int i = i0; i < i1; i++) mean += _raw[i];
            mean /= Math.Max(1, i1 - i0);
            for (int i = i0; i < i1; i++)
            {
                double y = _raw[i] - mean;
                view.Add(new Point(i / DemoFs, y));
                maxAbs = Math.Max(maxAbs, Math.Abs(y));
            }
            x1 = Math.Max(t, w); x0 = x1 - w;
        }
        else
        {
            for (int i = _speed.Count - 1; i >= 0; i--)
            {
                var p = _speed[i];
                if (p.X < x0) break;
                view.Add(p);
                maxAbs = Math.Max(maxAbs, Math.Abs(p.Y));
            }
            view.Reverse();
        }
        double ymax = NiceCeil(maxAbs * 1.15);
        TimeChart.SetData(view, x0, x1, -ymax, ymax);

    }

    private void RedrawSpectrum()
    {
        // 4. spekter: Welch (16 s okno) iz signala odstopanja hitrosti
        var spec = Spectrum.Welch(_raw, DemoFs, _specWin, 0.5, 0.2, 100.0, 4);
        // počasno osveževanje (povprečje okvirjev), da prikaz ne trza
        int key = spec.Count * 1000 + (int)_specWin;
        if (spec.Count > 0)
        {
            if (_specAvg == null || _specAvg.Length != spec.Count || _specKey != key)
            { _specAvg = spec.Select(q => q.Y).ToArray(); _specKey = key; }
            else
                for (int i = 0; i < spec.Count; i++) _specAvg[i] = _specAvg[i] * 0.65 + spec[i].Y * 0.35;
            spec = spec.Select((q, i) => new Point(q.X, _specAvg![i])).ToList();
        }
        UpdatePeriodicity(spec);
        double yTop = _wfRange;                                   // fiksna os kot pri T100 (0,15 / 0,5 / 1,0 %)
        SpecChart.XTicks = new[] { 0.2, 0.4, 0.6, 0.8, 1, 2, 4, 6, 8, 10, 20, 40, 60, 80, 100 };
        SpecChart.SetData(spec, 0.2, 100, 0, yTop);
    }

    /// <summary>Najizrazitejši vrh v spektru: frekvenca, amplituda in (če je izbran profil) možen vzrok.</summary>
    private void UpdatePeriodicity(List<Point> spec)
    {
        var cand = spec.Where(q => q.X >= 0.5).ToList();
        if (cand.Count < 10) { PeriodText.Text = ""; return; }
        var ys = cand.Select(q => q.Y).OrderBy(v => v).ToList();
        double median = ys[ys.Count / 2];
        var top = cand.OrderByDescending(q => q.Y).First();
        if (top.Y < 0.004 || top.Y < 5 * median)
        {
            PeriodText.Text = Loc.T("wfs.per.none");
            return;
        }
        string txt = $"{Loc.T("wfs.per")}: {top.X:0.00} Hz · {top.Y:0.000} %";
        if (_mech.Id != "none")
        {
            double ratio = _hz.Count > 0 ? _hz.Average(q => q.Y) / _refHz : 1.0;
            var m = _mech.Match(top.X, ratio, Math.Max(0.12, 1.0 / _specWin));
            if (m is { } mm)
                txt += $" · {Loc.T("wfs.per.cause")}: {(Loc.Lang == "sl" ? mm.Sl : mm.En)}";
        }
        PeriodText.Text = txt;
    }

    private static double NiceCeil(double v)
    {
        foreach (double s in new[] { 0.1, 0.2, 0.5, 1, 2, 3, 5, 10 })
            if (v <= s) return s;
        return Math.Ceiling(v);
    }

    // ---------- poročilo: vse tri meritve na enem grafu ----------
    private void View_Checked(object sender, RoutedEventArgs e)
    {
        _showReport = TagVal(sender) == 1;
        if (_ready) ApplyView();
    }

    private void Axes_Checked(object sender, RoutedEventArgs e)
    {
        _fixedAxes = TagVal(sender) == 1;
        if (_ready) RefreshReport();
    }

    private void RepText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready && _showReport) RefreshReport();
    }

    private void ApplyView()
    {
        var live = _showReport ? Visibility.Collapsed : Visibility.Visible;
        PanelSpeed.Visibility = live; PanelTime.Visibility = live;
        PanelWf.Visibility = live; PanelSpec.Visibility = live;
        ReportPanel.Visibility = _showReport ? Visibility.Visible : Visibility.Collapsed;
        if (_showReport) RefreshReport();
    }

    private static List<Point> Trend(IReadOnlyList<Point> pts)
    {
        var res = new List<Point>();
        int n = pts.Count;
        if (n < 2) return res;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var q in pts) { sx += q.X; sy += q.Y; sxx += q.X * q.X; sxy += q.X * q.Y; }
        double d = n * sxx - sx * sx;
        if (Math.Abs(d) < 1e-12) return res;
        double b = (n * sxy - sx * sy) / d, a = (sy - b * sx) / n;
        double x0 = pts[0].X, x1 = pts[n - 1].X;
        res.Add(new Point(x0, a + b * x0));
        res.Add(new Point(x1, a + b * x1));
        return res;
    }

    private void FillReportSeries(ReportChart ch)
    {
        string lin = Loc.T("rep.lin");
        ch.Series.Clear();
        ch.Series.Add(new ChartSeries { Key = "wrms", Name = "WRMS", Points = _wf });
        ch.Series.Add(new ChartSeries { Key = "din", Name = "DIN Wtd", Points = _pk });
        ch.Series.Add(new ChartSeries { Key = "hz", Name = "Hz", Points = _hz, Secondary = true });
        ch.Series.Add(new ChartSeries { Key = "t_wrms", Name = $"{lin} (WRMS)", Points = Trend(_wf), Trend = true });
        ch.Series.Add(new ChartSeries { Key = "t_din", Name = $"{lin} (DIN Wtd)", Points = Trend(_pk), Trend = true });
        ch.Series.Add(new ChartSeries { Key = "t_hz", Name = $"{lin} (Hz)", Points = Trend(_hz), Secondary = true, Trend = true });
        ch.FixedAxes = _fixedAxes;
        ch.CenterHz = Math.Abs(_refHz - 3150) < Math.Abs(_refHz - 3000) ? 3150 : 3000;
        ch.XMax = Math.Max(_durationMin * 60, _wf.Count > 0 ? _wf[_wf.Count - 1].X : 0);
        ch.Caption = $"{DeviceBox.Text}, {DateBox.Text}";
        ch.LeftTitle = "W&F %";
        ch.RightTitle = "Speed Hz";
        ch.XTitle = $"Time, sec ({_durationMin:0}min)";
    }

    private static string Stat(IReadOnlyList<Point> pts, string fmt, double refHz = 0)
    {
        if (pts.Count == 0) return "–";
        double mn = double.MaxValue, mx = double.MinValue, sum = 0;
        foreach (var q in pts) { mn = Math.Min(mn, q.Y); mx = Math.Max(mx, q.Y); sum += q.Y; }
        double avg = sum / pts.Count;
        string unit = refHz > 0 ? " Hz" : " %";
        string l1 = $"{Loc.T("rep.avg")} {avg.ToString(fmt, CultureInfo.CurrentCulture)}{unit}";
        if (refHz > 0) l1 += $" ({(avg / refHz - 1) * 100:+0.00;-0.00;0.00} %)";
        return $"{l1}\n{Loc.T("rep.min")} {mn.ToString(fmt, CultureInfo.CurrentCulture)} · {Loc.T("rep.max")} {mx.ToString(fmt, CultureInfo.CurrentCulture)}{unit}";
    }

    private void RefreshReport()
    {
        if (!_ready) return;
        FillReportSeries(RepChart);
        RepChart.Refresh();
        StatSpeed.Text = Stat(_hz, "0.0", _refHz);
        StatWrms.Text = Stat(_wf, "0.000");
        StatPeak.Text = Stat(_pk, "0.000");
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "TXT / CSV (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;

        var rows = new List<(double hz, double wr, double pk)>();
        foreach (var line in File.ReadLines(dlg.FileName))
        {
            var p = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) continue;
            if (!TryNum(p[0], out var hz) || !TryNum(p[1], out var wr) || !TryNum(p[2], out var pk)) continue;
            if (hz <= 0) continue;               // ničelne vrstice na koncu zapisa
            rows.Add((hz, wr, pk));
        }
        if (rows.Count < 2) return;

        if (_running) Stop();
        _speed.Clear(); _wf.Clear(); _pk.Clear(); _hz.Clear();
        double dt = _durationMin * 60.0 / rows.Count;   // vrstice enakomerno čez trajanje testa (npr. 45 min)
        for (int i = 0; i < rows.Count; i++)
        {
            double t = i * dt;
            _hz.Add(new Point(t, rows[i].hz));
            _speed.Add(new Point(t, (rows[i].hz / _refHz - 1) * 100));
            _wf.Add(new Point(t, rows[i].wr));
            _pk.Add(new Point(t, rows[i].pk));
        }
        _source = Path.GetFileName(dlg.FileName);
        DateBox.Text = File.GetLastWriteTime(dlg.FileName).ToString("dd.MM.yyyy");   // dan, ko je bila meritev opravljena
        string dev = _source.Split(',')[0].Trim();
        if (dev.Length > 0) DeviceBox.Text = dev;
        ViewReport.IsChecked = true;
        RefreshReport();
        StatusText.Text = _source;
    }

    private void ExportReportPng()
    {
        const int pw = 1880, ph = 1000;      // razmerje 1,88 kot graf v programu
        var ch = new ReportChart { Print = AppSettings.ReportLight, Width = pw, Height = ph };
        FillReportSeries(ch);
        ch.FixedAxes = true;                                   // izvoz ima vedno fiksne osi
        ch.Measure(new Size(pw, ph));
        ch.Arrange(new Rect(0, 0, pw, ph));
        ch.UpdateLayout();
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(ch);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        string name = string.Concat(DeviceBox.Text.Split(Path.GetInvalidFileNameChars()));
        string file = OutName.Next(OutDir(), name.Trim().Length > 0 ? "WFS Report " + name.Trim() : "WFS Report", ".png");
        using (var fs = File.Create(file)) enc.Save(fs);
        StatusText.Text = Loc.T("wfs.saved") + file;
    }

    // ---------- posnetek zaslona in izvoz ----------
    private static string OutDir() => AppSettings.OutDir();

    private void BtnShot_Click(object sender, RoutedEventArgs e)
    {
        if (!License.IsLicensed) { StatusText.Text = Loc.T("lic.limit"); return; }
        if (_showReport) { ExportReportPng(); return; }
        var dpi = VisualTreeHelper.GetDpi(this);
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
        string file = OutName.Next(OutDir(), "WFS Screen", ".png");
        using (var fs = File.Create(file)) enc.Save(fs);
        StatusText.Text = Loc.T("wfs.saved") + file;
    }

    private void BtnCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!License.IsLicensed) { StatusText.Text = Loc.T("lic.limit"); return; }
        var sb = new StringBuilder("time_s;speed_hz;speed_pct;wrms_pct;peak_pct\n");
        int cnt = Math.Min(Math.Min(_speed.Count, _hz.Count), Math.Min(_wf.Count, _pk.Count));
        for (int i = 0; i < cnt; i++)
            sb.AppendLine(string.Join(';',
                _speed[i].X.ToString("0.000", CultureInfo.InvariantCulture),
                _hz[i].Y.ToString("0.0", CultureInfo.InvariantCulture),
                _speed[i].Y.ToString("0.0000", CultureInfo.InvariantCulture),
                _wf[i].Y.ToString("0.0000", CultureInfo.InvariantCulture),
                _pk[i].Y.ToString("0.0000", CultureInfo.InvariantCulture)));
        string file = OutName.Next(OutDir(), "WFS Screen", ".csv", true);
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        StatusText.Text = Loc.T("wfs.saved") + file;
    }

    /// <summary>Shrani rezultat za skupno poročilo, ko zapustiš meritev.</summary>
    private void SaveSnapshot()
    {
        try
        {
            if (_wf.Count < 10 || _hz.Count < 10) return;
            var m = _model ?? _defModel;
            SessionReport.Wf = new WfSnapshot
            {
                Hz = new List<Point>(_hz), Wrms = new List<Point>(_wf), Peak = new List<Point>(_pk),
                RefHz = _refHz, DurationMin = _durationMin,
                Device = DeviceBox.Text, Date = DateBox.Text, Source = _source,
                Mech = _mech.Id == "none" ? "" : _mech.Title,
                Limit = m != null && m.Standard.StartsWith("WRMS", StringComparison.OrdinalIgnoreCase) ? m.WfMax : double.NaN,
                IsDemo = _source.StartsWith("SIM", StringComparison.OrdinalIgnoreCase),
            };
        }
        catch { }
    }

    public void Dispose()
    {
        SaveSnapshot();
        _timer.Stop();
        _asio?.Dispose();
        Loc.Changed -= ApplyLang;
    }
}
