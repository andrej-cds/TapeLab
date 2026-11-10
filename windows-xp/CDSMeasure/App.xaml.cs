using System.Windows;
using System.Windows.Threading;

namespace CDSMeasure;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Diag.LogStart();
        DispatcherUnhandledException += (s, a) =>
        {
            Diag.Log("NEOBDELANA NAPAKA: " + a.Exception);
            MessageBox.Show("Napaka / Error:\n" + a.Exception.Message + "\n\nLog: " + Diag.LogPath, "CDS TapeLab");
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, a) => Diag.Log("KRITIČNA NAPAKA: " + a.ExceptionObject);
        base.OnStartup(e);
    }
}
