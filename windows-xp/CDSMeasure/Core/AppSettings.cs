using System.Globalization;
using System.IO;

namespace CDSMeasure;

/// <summary>Imena izvoženih datotek: "Predpona_dd.mm.yy;XX.pripona", XX = zaporedna številka dneva (01, 02, …), ki se z novim datumom sama začne znova.</summary>
public static class OutName
{
    /// <param name="pair">true (za CSV): vzame številko zadnje slike istega dne, če ta še nima CSV; sicer naslednjo.</param>
    public static string Next(string dir, string prefix, string ext, bool pair = false)
    {
        string date = DateTime.Now.ToString("dd'.'MM'.'yy", CultureInfo.InvariantCulture);
        string head = prefix + "_" + date + ";";
        int max = 0; bool maxHasExt = false;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, head + "*"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (n.Length <= head.Length || !n.StartsWith(head, StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(n.Substring(head.Length), out int v)) continue;
                if (v > max) { max = v; maxHasExt = false; }
                if (v == max && string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase)) maxHasExt = true;
            }
        }
        catch { }
        int next = pair && max > 0 && !maxHasExt ? max : max + 1;
        return Path.Combine(dir, head + next.ToString("00", CultureInfo.InvariantCulture) + ext);
    }
}

/// <summary>Uporabniške nastavitve programa; shranjene v %AppData%\CDSTapeLab\settings.json.</summary>
public static class AppSettings
{
    /// <summary>Ime ASIO gonilnika; prazno = samodejno (Behringer / UMC, sicer prvi gonilnik).</summary>
    public static string AsioDriver { get; set; } = "";
    /// <summary>Prvi vhodni kanal (od 0) para L/D.</summary>
    public static int InputStart { get; set; } = 0;
    /// <summary>Prvi izhodni kanal (od 0) para L/D.</summary>
    public static int OutputStart { get; set; } = 0;
    /// <summary>Vzorčenje v Hz; 0 = samodejno (192, 96, 48 kHz).</summary>
    public static int SampleRate { get; set; } = 0;
    /// <summary>Nivo, ki ga ima sweep pri 0 dB (dBFS). −6 dBFS = privzeto; +6 dB je potem polna skala.</summary>
    public static double SweepRefDbfs { get; set; } = -6;
    /// <summary>Mapa za shranjevanje slik, CSV in WAV; prazno = Dokumenti\CDS TapeLab.</summary>
    public static string SaveDir { get; set; } = "";

    /// <summary>Izvožena slika poročila (W&F): false = temna podlaga, true = bela (za tisk).</summary>
    public static bool ReportLight { get; set; } = false;

    public static readonly int[] Rates = { 48000, 96000, 192000 };

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDSTapeLab", "settings.ini");

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CDS TapeLab");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var kv = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string V(string k) { string v; return kv.TryGetValue(k, out v) ? v : ""; }
            int I(string k) { int r; return int.TryParse(V(k), out r) ? r : 0; }
            AsioDriver = V("AsioDriver");
            InputStart = Math.Max(0, I("InputStart"));
            OutputStart = Math.Max(0, I("OutputStart"));
            int sr = I("SampleRate");
            SampleRate = Array.IndexOf(Rates, sr) >= 0 ? sr : 0;
            double lv;
            SweepRefDbfs = double.TryParse(V("SweepRefDbfs"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lv)
                           && lv >= -24 && lv <= -6 ? lv : -6;
            SaveDir = V("SaveDir");
            ReportLight = V("ReportLight") == "1";
        }
        catch { /* pokvarjena datoteka: ostanejo privzete vrednosti */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllLines(FilePath, new[]
            {
                "AsioDriver=" + AsioDriver,
                "InputStart=" + InputStart.ToString(inv),
                "OutputStart=" + OutputStart.ToString(inv),
                "SampleRate=" + SampleRate.ToString(inv),
                "SweepRefDbfs=" + SweepRefDbfs.ToString(inv),
                "SaveDir=" + SaveDir,
                "ReportLight=" + (ReportLight ? "1" : "0"),
            });
        }
        catch { }
    }

    /// <summary>Mapa za izvoz; če izbrana ni dosegljiva, se uporabi privzeta.</summary>
    public static string OutDir()
    {
        string dir = string.IsNullOrWhiteSpace(SaveDir) ? DefaultDir : SaveDir;
        try { Directory.CreateDirectory(dir); }
        catch { dir = DefaultDir; Directory.CreateDirectory(dir); }
        return dir;
    }
}
