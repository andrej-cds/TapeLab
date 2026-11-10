using System.Text;

namespace CDSMeasure;

/// <summary>Dnevnik delovanja (XP izdaja): %AppData%\CDSTapeLab\log.txt. Testnik naj pošlje to datoteko, če kaj ne dela.</summary>
public static class Diag
{
    private static readonly object L = new object();
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDSTapeLab", "log.txt");

    public static void Log(string msg)
    {
        try
        {
            lock (L)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                // datoteka naj ne raste v nedogled
                try { if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 500000) File.Delete(LogPath); } catch { }
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void LogStart()
    {
        Log("==== zagon CDS TapeLab (XP izdaja) 0.6.0 ====");
        Log("OS: " + Environment.OSVersion + "  64-bit OS: " + Environment.Is64BitOperatingSystem + "  64-bit proces: " + Environment.Is64BitProcess);
        Log("CLR: " + Environment.Version + "  procesorji: " + Environment.ProcessorCount);
        Log("Kultura: " + System.Globalization.CultureInfo.CurrentCulture.Name);
        try { Log("ASIO gonilniki: " + string.Join(" | ", CDSMeasure.Core.AsioCapture.Drivers())); } catch (Exception ex) { Log("ASIO seznam napaka: " + ex.Message); }
    }
}
