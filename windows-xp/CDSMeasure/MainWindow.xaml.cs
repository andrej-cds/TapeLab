using System.Windows;
using System.Windows.Controls;
using CDSMeasure.Views;

namespace CDSMeasure;

public partial class MainWindow : Window
{
    private string _titleKey = "";

    public MainWindow()
    {
        License.Load();
        AppSettings.Load();
        InitializeComponent();
        Loc.Changed += ApplyLang;
        ShowStart();
    }

    private void ShowStart()
    {
        (Host.Content as IDisposable)?.Dispose();
        var v = new StartView();
        v.Selected += Open;
        v.ReportClicked += () => { new ReportWindow { Owner = this }.ShowDialog(); };
        Host.Content = v;
        BackBtn.Visibility = Visibility.Collapsed;
        _titleKey = "";
        ApplyLang();
    }

    private void Open(string id)
    {
        (Host.Content as IDisposable)?.Dispose();
        UserControl v = id switch
        {
            "wfs" => new WfsView(),
            "pb" => new PbView(),
            "rec" => new RecView(),
            "az" => new AzimuthView(),
            "volt" => new VoltView(),
            _ => new SoonView(),
        };
        _titleKey = id switch { "wfs" => "tile.wfs.t", "pb" => "tile.pb.t", "az" => "tile.az.t", "volt" => "tile.volt.t", _ => "tile.rec.t" };
        Host.Content = v;
        BackBtn.Visibility = Visibility.Visible;
        ApplyLang();
    }

    private void ApplyLang()
    {
        SubText.Text = Loc.T("app.sub");
        BackBtn.Content = Loc.T("back");
        LicBtn.Content = Loc.T("lic.btn");
        HelpBtn.Content = Loc.T("help.btn");
        SetBtn.Content = Loc.T("set.btn");
        AboutBtn.Content = Loc.T("about.btn");
        TitleText.Text = _titleKey == "" ? Loc.T("start.title") : Loc.T(_titleKey);
    }

    private void LicBtn_Click(object sender, RoutedEventArgs e)
    {
        new LicenseWindow { Owner = this }.ShowDialog();
        if (_titleKey == "") ShowStart();     // osveži prikaz licence na začetnem zaslonu
    }
    private void HelpBtn_Click(object sender, RoutedEventArgs e) => AboutWindow.OpenManual(this);
    private void SetBtn_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void AboutBtn_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void BackBtn_Click(object sender, RoutedEventArgs e) => ShowStart();
    private void LangSl_Checked(object sender, RoutedEventArgs e) => Loc.Set("sl");
    private void LangEn_Checked(object sender, RoutedEventArgs e) => Loc.Set("en");
}
