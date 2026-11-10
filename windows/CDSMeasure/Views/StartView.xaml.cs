using CDSMeasure.Core;
using System.Windows;
using System.Windows.Controls;

namespace CDSMeasure.Views;

public partial class StartView : UserControl, IDisposable
{
    public event Action<string>? Selected;
    public event Action? ReportClicked;

    public StartView()
    {
        InitializeComponent();
        Loc.Changed += ApplyLang;
        ApplyLang();
    }

    private void ApplyLang()
    {
        WfsT.Text = Loc.T("tile.wfs.t"); WfsD.Text = Loc.T("tile.wfs.d");
        PbT.Text = Loc.T("tile.pb.t"); PbD.Text = Loc.T("tile.pb.d");
        RecT.Text = Loc.T("tile.rec.t"); RecD.Text = Loc.T("tile.rec.d");
        AzT.Text = Loc.T("tile.az.t"); AzD.Text = Loc.T("tile.az.d");
        VoltT.Text = Loc.T("tile.volt.t"); VoltD.Text = Loc.T("tile.volt.d");
        CardL.Text = Loc.T("card"); CardV.Text = CardText();
        LicL.Text = Loc.T("lic"); LicV.Text = License.IsLicensed ? License.Name : "DEMO";
        ReportBtn.Content = Loc.T("sr.btn");
        ReportBtn.IsEnabled = SessionReport.HasAny;
        string Mark(bool ok) => ok ? "✓" : "–";
        ReportInfo.Text = $"W&F {Mark(SessionReport.Wf != null)}  ·  Playback {Mark(SessionReport.Pb != null)}  ·  Recording {Mark(SessionReport.Rec != null)}  ·  Azimuth {Mark(SessionReport.Az != null)}";
    }

    /// <summary>Napis na dnu: dejanski gonilnik in vzorčenje iz nastavitev (ali samodejna izbira).</summary>
    private static string CardText()
    {
        string? drv = !string.IsNullOrEmpty(AppSettings.AsioDriver) ? AppSettings.AsioDriver : AsioCapture.FindAuto();
        string rate = AppSettings.SampleRate > 0 ? (AppSettings.SampleRate / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz"
                                                 : Loc.T("set.rate.auto");
        return (drv ?? "—") + " · ASIO · " + rate;
    }

    private void Tile_Click(object sender, RoutedEventArgs e) =>
        Selected?.Invoke((string)((Button)sender).Tag);

    private void Report_Click(object sender, RoutedEventArgs e) => ReportClicked?.Invoke();

    public void Dispose() => Loc.Changed -= ApplyLang;
}
