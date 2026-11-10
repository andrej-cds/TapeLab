using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CDSMeasure;

/// <summary>Skupno poročilo: vnos podatkov o stranki in kasetofonu ter izdelava PDF iz meritev te seje (brez XAML).</summary>
public sealed class ReportWindow : Window
{
    private static object Res(string key) => Application.Current.Resources[key];

    private readonly TextBox _cust = new(), _deck = new(), _serial = new(), _date = new(), _notes = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _make = new(), _close = new();

    public ReportWindow()
    {
        Title = Loc.T("sr.title");
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Res("BgBrush"); Foreground = (Brush)Res("TextBrush");
        FontFamily = (FontFamily)Res("FontBody");

        var root = new StackPanel { Margin = new Thickness(24) };

        root.Children.Add(new TextBlock { Text = Loc.T("sr.hint"), Foreground = (Brush)Res("DimBrush"), Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(StateLine("tile.wfs.t", SessionReport.Wf != null));
        root.Children.Add(StateLine("tile.pb.t", SessionReport.Pb != null));
        root.Children.Add(StateLine("tile.rec.t", SessionReport.Rec != null));
        root.Children.Add(StateLine("tile.az.t", SessionReport.Az != null));

        _cust.Text = SessionReport.Customer;
        _deck.Text = !string.IsNullOrWhiteSpace(SessionReport.Deck) ? SessionReport.Deck : (SessionReport.Wf?.Device ?? "");
        _serial.Text = SessionReport.Serial;
        _date.Text = !string.IsNullOrWhiteSpace(SessionReport.Date) ? SessionReport.Date
                     : (!string.IsNullOrWhiteSpace(SessionReport.Wf?.Date) ? SessionReport.Wf!.Date : DateTime.Now.ToString("dd.MM.yyyy"));
        _notes.Text = SessionReport.Notes;
        _notes.AcceptsReturn = true; _notes.TextWrapping = TextWrapping.Wrap; _notes.Height = 72;
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        root.Children.Add(Field("sr.cust", _cust));
        root.Children.Add(Field("sr.deck", _deck));
        root.Children.Add(Field("sr.serial", _serial));
        root.Children.Add(Field("sr.date", _date));
        root.Children.Add(Field("sr.notes", _notes));

        _make.Content = Loc.T("sr.make"); _make.Style = (Style)Res("CdsPrimary"); _make.MinWidth = 150;
        _close.Content = Loc.T("sr.close"); _close.Style = (Style)Res("CdsButton"); _close.Margin = new Thickness(8, 0, 0, 0);
        _make.IsEnabled = SessionReport.HasAny;
        _make.Click += (_, _) => Make();
        _close.Click += (_, _) => Close();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        bar.Children.Add(_make); bar.Children.Add(_close);
        root.Children.Add(bar);
        root.Children.Add(_status);
        if (!SessionReport.HasAny)
        {
            _status.Text = Loc.T("sr.none");
            _status.Foreground = (Brush)Res("DimBrush");
        }
        Content = root;
    }

    private static UIElement StateLine(string titleKey, bool done)
    {
        return new TextBlock
        {
            Text = (done ? "✓  " : "–  ") + Loc.T(titleKey),
            Foreground = (Brush)Res(done ? "GoldBrush" : "DimBrush"),
            Margin = new Thickness(0, 2, 0, 2),
        };
    }

    private static UIElement Field(string labelKey, TextBox box)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(new TextBlock { Text = Loc.T(labelKey), Foreground = (Brush)Res("DimBrush"), FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
        box.Style = (Style)Res("CdsTextBox");
        sp.Children.Add(box);
        return sp;
    }

    private void Make()
    {
        if (!License.IsLicensed)
        {
            _status.Foreground = (Brush)Res("RedBrush");
            _status.Text = Loc.T("lic.limit");
            return;
        }
        SessionReport.Customer = _cust.Text; SessionReport.Deck = _deck.Text; SessionReport.Serial = _serial.Text;
        SessionReport.Date = _date.Text; SessionReport.Notes = _notes.Text;
        try
        {
            _make.IsEnabled = false;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            string file = ReportPdf.Create(AppSettings.OutDir());
            _status.Foreground = (Brush)Res("GoldBrush");
            _status.Text = Loc.T("wfs.saved") + file;
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            _status.Foreground = (Brush)Res("RedBrush");
            _status.Text = ex.Message;
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            _make.IsEnabled = SessionReport.HasAny;
        }
    }
}
