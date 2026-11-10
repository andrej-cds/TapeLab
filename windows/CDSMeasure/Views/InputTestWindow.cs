using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CDSMeasure.Core;
using NAudio.Wave;

namespace CDSMeasure;

/// <summary>Test vhoda: odpre izbrani ASIO gonilnik in pokaže raven (dBFS) na VSEH vhodnih kanalih, da se vidi, na katerem paru je signal.
/// Namenjeno kartic z veliko kanali (npr. kX Audio), kjer line-in ni nujno na prvem paru.</summary>
public sealed class InputTestWindow : Window
{
    private const int MaxCh = 32;
    private const double Floor = -80.0;      // dBFS na levem robu stolpca
    private const double BarWidth = 380;

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private readonly string _driver;
    private readonly int _wantRate;
    private AsioOut? _asio;
    private int _ch;
    private double[] _cur = new double[0];       // največja absolutna vrednost od zadnjega branja (nastavlja ASIO nit)
    private double[] _shown = new double[0];     // prikazana vrednost z zadrževanjem
    private Border[] _bars = new Border[0];
    private TextBlock[] _db = new TextBlock[0];
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _best = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly StackPanel _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public InputTestWindow(string driver, int sampleRate)
    {
        _driver = driver; _wantRate = sampleRate;
        Title = Loc.T("it.title");
        Width = 560; Height = 640; MinHeight = 360; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Res("BgBrush"); Foreground = Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["FontBody"];

        _info.Foreground = Res("DimBrush"); _info.FontSize = 12;
        _best.Foreground = Res("GoldBrush");

        var close = new Button { Content = Loc.T("sr.close"), MinWidth = 110, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["CdsButton"] };
        close.Click += (_, _) => Close();

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel();
        top.Children.Add(_info); top.Children.Add(_best);
        Grid.SetRow(top, 0); root.Children.Add(top);
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows };
        Grid.SetRow(sv, 1); root.Children.Add(sv);
        Grid.SetRow(close, 2); root.Children.Add(close);
        Content = root;

        Loaded += (_, _) => Start();
        Closed += (_, _) => StopAll();
        _timer.Tick += (_, _) => Tick();
    }

    private void Start()
    {
        string lastError = "";
        var rates = new List<int>();
        if (_wantRate > 0) rates.Add(_wantRate);
        else rates.AddRange(new[] { 48000, 96000, 192000, 44100 });
        foreach (int rate in rates)
        {
            AsioOut? asio = null;
            try
            {
                asio = new AsioOut(_driver);
                int inCnt = asio.DriverInputChannelCount;
                int ch = Math.Max(1, Math.Min(MaxCh, inCnt));
                asio.InputChannelOffset = 0;
                asio.InitRecordAndPlayback(null, ch, rate);
                _ch = ch;
                _cur = new double[ch]; _shown = new double[ch];
                BuildRows(ch);
                asio.AudioAvailable += OnAudio;
                _asio = asio;
                asio.Play();
                _info.Text = _driver + "  ·  " + (rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz  ·  "
                           + ch + " " + Loc.T("it.channels") + "\n" + Loc.T("it.hint");
                _timer.Start();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                try { if (asio != null) asio.Dispose(); } catch { }
                _asio = null;
            }
        }
        _info.Text = Loc.T("wfs.asioerr") + lastError;
    }

    private void BuildRows(int ch)
    {
        _rows.Children.Clear();
        _bars = new Border[ch]; _db = new TextBlock[ch];
        for (int i = 0; i < ch; i++)
        {
            var g = new Grid { Margin = new Thickness(0, i % 2 == 0 && i > 0 ? 10 : 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth + 2) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lab = new TextBlock { Text = Loc.T("it.in") + " " + (i + 1) + (i % 2 == 0 ? "  (L)" : "  (R)"), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("DimBrush"), FontSize = 12 };
            var frame = new Border { Width = BarWidth + 2, Height = 14, BorderBrush = Res("LineBrush"), BorderThickness = new Thickness(1), Background = Res("PanelBrush") };
            var bar = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, Background = Res("GoldBrush") };
            frame.Child = bar;
            var db = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)Application.Current.Resources["FontMono"], FontSize = 12 };
            Grid.SetColumn(lab, 0); Grid.SetColumn(frame, 1); Grid.SetColumn(db, 2);
            g.Children.Add(lab); g.Children.Add(frame); g.Children.Add(db);
            _bars[i] = bar; _db[i] = db;
            _rows.Children.Add(g);
        }
    }

    // ASIO nit: samo največja absolutna vrednost po kanalu
    private void OnAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        try
        {
            int c = e.InputBuffers.Length, n = e.SamplesPerBuffer;
            if (c <= 0 || n <= 0) return;
            var b = new float[n * c];
            e.GetAsInterleavedSamples(b);
            var cur = _cur;
            for (int k = 0; k < c && k < cur.Length; k++)
            {
                double m = 0;
                for (int i = 0; i < n; i++) { double a = Math.Abs(b[i * c + k]); if (a > m) m = a; }
                if (m > cur[k]) cur[k] = m;
            }
        }
        catch { }
    }

    private static double ToDb(double lin) => lin <= 1e-6 ? -120.0 : 20.0 * Math.Log10(lin);

    private void Tick()
    {
        int ch = _ch;
        if (ch == 0) return;
        double best = Floor; int bestPair = -1;
        for (int i = 0; i < ch; i++)
        {
            double v = _cur[i]; _cur[i] = 0;
            _shown[i] = Math.Max(v, _shown[i] * 0.85);
            double db = ToDb(_shown[i]);
            double frac = Math.Clamp((db - Floor) / (0 - Floor), 0, 1);
            _bars[i].Width = frac * BarWidth;
            _bars[i].Background = db > -1 ? Res("RedBrush") : Res("GoldBrush");
            _db[i].Text = db <= -119 ? "—" : db.ToString("0.0", CultureInfo.InvariantCulture) + " dBFS";
            _db[i].Foreground = db > -60 ? Res("TextBrush") : Res("DimBrush");
        }
        for (int p = 0; p + 1 < ch; p += 2)
        {
            double db = Math.Max(ToDb(_shown[p]), ToDb(_shown[p + 1]));
            if (db > best) { best = db; bestPair = p; }
        }
        if (ch == 1) { best = ToDb(_shown[0]); bestPair = 0; }
        if (bestPair >= 0 && best > -60)
            _best.Text = (ch == 1 ? Loc.T("it.strong1") : Loc.T("it.strong") + " " + (bestPair + 1) + " + " + (bestPair + 2) + "  (" + best.ToString("0.0", CultureInfo.InvariantCulture) + " dBFS)");
        else
            _best.Text = Loc.T("it.none");
    }

    private void StopAll()
    {
        _timer.Stop();
        var a = _asio; _asio = null;
        if (a != null)
        {
            try { a.AudioAvailable -= OnAudio; a.Stop(); } catch { }
            try { a.Dispose(); } catch { }
            GC.Collect(); GC.WaitForPendingFinalizers();
            Thread.Sleep(250);        // gonilnik naj se sprosti, preden ga odpre meritev
        }
    }
}
