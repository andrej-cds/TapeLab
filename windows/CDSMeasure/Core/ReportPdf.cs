using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CDSMeasure.Controls;

namespace CDSMeasure;

/// <summary>
/// Skupno poročilo meritev kot PDF (A4, temen slog CDS). Strani se narišejo kot WPF elementi, upodobijo v sliko (JPEG)
/// in zapišejo v preprost PDF brez zunanjih knjižnic.
/// </summary>
public static class ReportPdf
{
    private const double PW = 794, PH = 1123, M = 40;      // A4 pri 96 dpi

    private static Brush Br(string key) => (Brush)Application.Current.Resources[key];
    private static FontFamily Ff(string key) => (FontFamily)Application.Current.Resources[key];

    private static TextBlock Tx(string s, double size, string brush = "TextBrush", string font = "FontBody",
                                Thickness? margin = null, bool wrap = false)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = Br(brush), FontFamily = Ff(font) };
        if (margin.HasValue) t.Margin = margin.Value;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    // ---------- gradniki strani ----------
    private static UIElement Header()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        try
        {
            var img = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
        }
        catch { }
        var r = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t1 = Tx("CDS TAPELAB", 18, "GoldBrush", "FontHead"); t1.HorizontalAlignment = HorizontalAlignment.Right;
        var t2 = Tx(Loc.T("sr.pdf.title"), 12, "DimBrush"); t2.HorizontalAlignment = HorizontalAlignment.Right;
        r.Children.Add(t1); r.Children.Add(t2);
        Grid.SetColumn(r, 1); g.Children.Add(r);
        sp.Children.Add(g);
        sp.Children.Add(new Border { Height = 1, Background = Br("GoldBrush") });
        return sp;
    }

    private static UIElement Info()
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Add(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Tx(label, 11, "DimBrush", margin: new Thickness(0, 4, 0, 2));
            var v = Tx(value, 13, wrap: true, margin: new Thickness(0, 2, 0, 2));
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v); row++;
        }
        var wf = SessionReport.Wf;
        Add(Loc.T("sr.cust"), SessionReport.Customer);
        Add(Loc.T("sr.deck"), DeckName());
        Add(Loc.T("sr.serial"), SessionReport.Serial);
        Add(Loc.T("sr.date"), DateText());
        Add(Loc.T("sr.mech"), wf?.Mech ?? "");
        var srcs = new List<string>();
        foreach (var s in new[] { wf?.Source, SessionReport.Pb?.Source, SessionReport.Rec?.Source, SessionReport.Az?.Source })
            if (!string.IsNullOrWhiteSpace(s) && !srcs.Contains(s!)) srcs.Add(s!);
        Add(Loc.T("sr.src"), string.Join("\n", srcs));
        Add(Loc.T("sr.notes"), SessionReport.Notes);
        return g;
    }

    private static string DeckName() =>
        !string.IsNullOrWhiteSpace(SessionReport.Deck) ? SessionReport.Deck.Trim() : (SessionReport.Wf?.Device ?? "");

    private static string DateText() =>
        !string.IsNullOrWhiteSpace(SessionReport.Date) ? SessionReport.Date.Trim() : DateTime.Now.ToString("dd.MM.yyyy");

    private static UIElement Section(string title) =>
        Tx(title, 17, "GoldBrush", "FontHead", new Thickness(0, 14, 0, 6));

    private static UIElement NotDone() => new Border
    {
        Height = 70, Background = Br("Panel2Brush"), BorderBrush = Br("LineBrush"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 6),
        Child = new TextBlock
        {
            Text = Loc.T("sr.notdone"), Foreground = Br("DimBrush"), FontSize = 13, FontFamily = Ff("FontBody"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Border Box(string title, string text, Thickness margin)
    {
        var sp = new StackPanel();
        sp.Children.Add(Tx(title, 11, "DimBrush"));
        sp.Children.Add(Tx(text, 12, "TextBrush", "FontMono", new Thickness(0, 4, 0, 0)));
        return new Border
        {
            Background = Br("Panel2Brush"), BorderBrush = Br("LineBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = margin, Child = sp,
        };
    }

    private static string Stat(IReadOnlyList<Point> pts, string fmt, double refHz = 0)
    {
        if (pts.Count == 0) return "–";
        double mn = double.MaxValue, mx = double.MinValue, sum = 0;
        foreach (var q in pts) { mn = Math.Min(mn, q.Y); mx = Math.Max(mx, q.Y); sum += q.Y; }
        double avg = sum / pts.Count;
        var ci = CultureInfo.CurrentCulture;
        string unit = refHz > 0 ? " Hz" : " %";
        string l1 = $"{Loc.T("rep.avg")} {avg.ToString(fmt, ci)}{unit}";
        if (refHz > 0) l1 += $" ({(avg / refHz - 1) * 100:+0.00;-0.00;0.00} %)";
        return $"{l1}\n{Loc.T("rep.min")} {mn.ToString(fmt, ci)} · {Loc.T("rep.max")} {mx.ToString(fmt, ci)}{unit}";
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

    private static UIElement WfBlock(WfSnapshot w)
    {
        var sp = new StackPanel();
        if (w.IsDemo) sp.Children.Add(Tx(Loc.T("sr.demo"), 11, "RedBrush", margin: new Thickness(0, 0, 0, 4)));

        var grid = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        grid.Children.Add(Box(Loc.T("rep.speed"), Stat(w.Hz, "0.0", w.RefHz), new Thickness(0, 0, 5, 0)));
        grid.Children.Add(Box(Loc.T("rep.wrms"), Stat(w.Wrms, "0.000"), new Thickness(3, 0, 3, 0)));
        grid.Children.Add(Box(Loc.T("rep.peak"), Stat(w.Peak, "0.000"), new Thickness(5, 0, 0, 0)));
        sp.Children.Add(grid);

        if (!double.IsNaN(w.Limit) && w.Wrms.Count > 0)
        {
            double mx = w.Wrms.Max(p => p.Y);
            var ci = CultureInfo.CurrentCulture;
            sp.Children.Add(Tx($"{Loc.T("sr.limit")}: {w.Limit.ToString("0.000", ci)} %   ·   {Loc.T("sr.measmax")}: {mx.ToString("0.000", ci)} %",
                               12, "GoldLightBrush", "FontMono", new Thickness(0, 0, 0, 6)));
        }

        string lin = Loc.T("rep.lin");
        var ch = new ReportChart { Width = 1500, Height = 798, Print = false };      // razmerje 1,88 kot v programu; na stran se zmanjša z Viewbox
        ch.Series.Add(new ChartSeries { Key = "wrms", Name = "WRMS", Points = w.Wrms });
        ch.Series.Add(new ChartSeries { Key = "din", Name = "DIN Wtd", Points = w.Peak });
        ch.Series.Add(new ChartSeries { Key = "hz", Name = "Hz", Points = w.Hz, Secondary = true });
        ch.Series.Add(new ChartSeries { Key = "t_wrms", Name = $"{lin} (WRMS)", Points = Trend(w.Wrms), Trend = true });
        ch.Series.Add(new ChartSeries { Key = "t_din", Name = $"{lin} (DIN Wtd)", Points = Trend(w.Peak), Trend = true });
        ch.Series.Add(new ChartSeries { Key = "t_hz", Name = $"{lin} (Hz)", Points = Trend(w.Hz), Secondary = true, Trend = true });
        ch.FixedAxes = true;                                   // osi so vedno enake (0–0,2 %, ±50 Hz okoli nazivne hitrosti)
        ch.CenterHz = Math.Abs(w.RefHz - 3150) < Math.Abs(w.RefHz - 3000) ? 3150 : 3000;
        ch.XMax = Math.Max(w.DurationMin * 60, w.Wrms.Count > 0 ? w.Wrms[w.Wrms.Count - 1].X : 0);
        ch.Caption = $"{DeckName()}, {DateText()}";
        ch.LeftTitle = "W&F %";
        ch.RightTitle = "Speed Hz";
        ch.XTitle = $"Time, sec ({w.DurationMin:0}min)";
        sp.Children.Add(new Viewbox { Width = PW - 2 * M, Height = (PW - 2 * M) / 1.88, Stretch = Stretch.Fill, Child = ch });
        return sp;
    }

    private static UIElement PbBlock(PbSnapshot p)
    {
        var ch = new BandChart
        {
            Width = PW - 2 * M, Height = 380, YTitle = p.YTitle, ShowRight = p.ShowRight,
            Caption = $"{DeckName()}, {DateText()}",
        };
        ch.SetSeries(p.Series, p.Top, p.Span);
        return ch;
    }

    private static UIElement RecBlock(RecSnapshot r)
    {
        var ch = new FrChart { Width = PW - 2 * M, Height = 400, Caption = $"{DeckName()}, {DateText()}" };
        ch.SetSeries(r.Series, r.Auto);
        return ch;
    }

    private static UIElement AzBlock(AzSnapshot a)
    {
        var inv = CultureInfo.InvariantCulture;
        var sp = new StackPanel();
        var grid = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        grid.Children.Add(Box(Loc.T("sr.az.phase"), a.PhaseDeg.ToString("+0.0;-0.0;0.0", inv) + " °", new Thickness(0, 0, 5, 0)));
        grid.Children.Add(Box(Loc.T("sr.az.delay"), a.DelayUs.ToString("+0.00;-0.00;0.00", inv) + " µs", new Thickness(3, 0, 3, 0)));
        grid.Children.Add(Box(Loc.T("sr.az.tone"), (a.FreqHz / 1000).ToString("0.00", inv) + " kHz", new Thickness(5, 0, 0, 0))); 
        sp.Children.Add(grid);
        sp.Children.Add(Tx($"{Loc.T("sr.az.levels")}:  L {a.LevelL.ToString("0.0", inv)}   R {a.LevelR.ToString("0.0", inv)}   ·   " + string.Format(Loc.T("sr.az.zone"), a.ZoneUs.ToString("0", inv)),
                           11, "DimBrush", margin: new Thickness(0, 0, 0, 8)));
        double zoneDeg = a.ZoneUs * 1e-6 * 360.0 * a.FreqHz;
        var gauge = new AzimuthGauge { Width = PW - 2 * M, Height = 140, Margin = new Thickness(0, 0, 0, 8) };
        gauge.Set(a.PhaseDeg, true, a.RangeDeg, zoneDeg);
        sp.Children.Add(gauge);
        var tr = new PhaseTrace { Width = PW - 2 * M, Height = 260 };
        double t1 = 0;
        foreach (var q in a.Trace) { tr.Add(q.X, q.Y); t1 = Math.Max(t1, q.X); }
        tr.Set(a.RangeDeg, zoneDeg, t1);
        sp.Children.Add(tr);
        return sp;
    }

    private static FrameworkElement Page(UIElement body, int n, int total)
    {
        var root = new DockPanel { Width = PW, Height = PH, Background = Br("BgBrush"), LastChildFill = true };
        var footer = new Grid { Margin = new Thickness(M, 0, M, 22) };
        footer.Children.Add(Tx("© " + DateTime.Now.Year + " Cassette Deck Service, Ljubljana  ·  www.cassettedeck-service.com", 10, "DimBrush"));
        var pg = Tx($"{Loc.T("sr.page")} {n} / {total}", 10, "DimBrush");
        pg.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(pg);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new Border { Padding = new Thickness(M, 30, M, 0), Child = body });
        return root;
    }

    // ---------- izdelava ----------
    /// <summary>Ustvari PDF v mapi outDir in vrne pot do datoteke.</summary>
    public static string Create(string outDir)
    {
        var p1 = new StackPanel();
        p1.Children.Add(Header());
        p1.Children.Add(Info());
        p1.Children.Add(Section(Loc.T("tile.wfs.t")));
        p1.Children.Add(SessionReport.Wf != null ? WfBlock(SessionReport.Wf) : NotDone());

        var bodies = new List<UIElement> { p1 };
        if (SessionReport.Pb != null || SessionReport.Rec != null)
        {
            var p2 = new StackPanel();
            p2.Children.Add(Header());
            p2.Children.Add(Section(Loc.T("tile.pb.t")));
            p2.Children.Add(SessionReport.Pb != null ? PbBlock(SessionReport.Pb) : NotDone());
            p2.Children.Add(Section(Loc.T("tile.rec.t")));
            p2.Children.Add(SessionReport.Rec != null ? RecBlock(SessionReport.Rec) : NotDone());
            bodies.Add(p2);
        }
        if (SessionReport.Az != null)
        {
            var p3 = new StackPanel();
            p3.Children.Add(Header());
            p3.Children.Add(Section(Loc.T("tile.az.t")));
            p3.Children.Add(AzBlock(SessionReport.Az));
            bodies.Add(p3);
        }

        var jpgs = new List<byte[]>();
        var sizes = new List<(int W, int H)>();
        for (int i = 0; i < bodies.Count; i++)
        {
            var page = Page(bodies[i], i + 1, bodies.Count);
            jpgs.Add(RenderJpeg(page, out int w, out int h));
            sizes.Add((w, h));
        }

        string name = string.Concat(DeckName().Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0) name = "report";
        string file = OutName.Next(outDir, "CDS Report", ".pdf");
        WritePdf(file, jpgs, sizes);
        return file;
    }

    private static byte[] RenderJpeg(FrameworkElement page, out int w, out int h)
    {
        page.Measure(new Size(PW, PH));
        page.Arrange(new Rect(0, 0, PW, PH));
        page.UpdateLayout();
        const double scale = 2;
        w = (int)(PW * scale); h = (int)(PH * scale);
        var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(page);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    // ---------- minimalni zapis PDF (ena slika na stran) ----------
    private static void WritePdf(string path, List<byte[]> jpgs, List<(int W, int H)> sizes)
    {
        var inv = CultureInfo.InvariantCulture;
        const double wPt = 595.276, hPt = 841.89;
        int n = jpgs.Count;
        int info = 3 + 3 * n;                       // 1 katalog, 2 strani, nato strani/vsebina/slika, na koncu info
        var offs = new long[info + 1];
        using var ms = new MemoryStream();

        void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Begin(int id) { offs[id] = ms.Position; Raw($"{id} 0 obj\n"); }
        void End() => Raw("endobj\n");

        Raw("%PDF-1.4\n");
        Begin(1); Raw("<< /Type /Catalog /Pages 2 0 R >>\n"); End();
        var kids = new StringBuilder();
        for (int i = 0; i < n; i++) kids.Append(3 + 3 * i).Append(" 0 R ");
        Begin(2); Raw($"<< /Type /Pages /Count {n} /Kids [ {kids}] >>\n"); End();

        for (int i = 0; i < n; i++)
        {
            int pg = 3 + 3 * i, ct = pg + 1, im = pg + 2;
            Begin(pg);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {wPt.ToString("0.###", inv)} {hPt.ToString("0.###", inv)}] " +
                $"/Resources << /XObject << /Im0 {im} 0 R >> >> /Contents {ct} 0 R >>\n");
            End();

            string content = $"q {wPt.ToString("0.###", inv)} 0 0 {hPt.ToString("0.###", inv)} 0 0 cm /Im0 Do Q\n";
            Begin(ct); Raw($"<< /Length {content.Length} >>\nstream\n{content}endstream\n"); End();

            Begin(im);
            Raw($"<< /Type /XObject /Subtype /Image /Width {sizes[i].W} /Height {sizes[i].H} /ColorSpace /DeviceRGB " +
                $"/BitsPerComponent 8 /Filter /DCTDecode /Length {jpgs[i].Length} >>\nstream\n");
            ms.Write(jpgs[i], 0, jpgs[i].Length);
            Raw("\nendstream\n");
            End();
        }

        Begin(info); Raw("<< /Title (CDS TapeLab measurement report) /Producer (CDS TapeLab) /Creator (CDS TapeLab) >>\n"); End();

        long xref = ms.Position;
        Raw($"xref\n0 {info + 1}\n0000000000 65535 f \n");
        for (int id = 1; id <= info; id++) Raw(offs[id].ToString("0000000000", inv) + " 00000 n \n");
        Raw($"trailer\n<< /Size {info + 1} /Root 1 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        File.WriteAllBytes(path, ms.ToArray());
    }
}
