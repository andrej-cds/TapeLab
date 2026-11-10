using System.Globalization;
using System.IO;
using System.Text.Json;

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

    private sealed class Data
    {
        public string AsioDriver { get; set; } = "";
        public int InputStart { get; set; }
        public int OutputStart { get; set; }
        public int SampleRate { get; set; }
        public double SweepRefDbfs { get; set; } = -6;
        public string SaveDir { get; set; } = "";
        public bool ReportLight { get; set; }
    }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDSTapeLab", "settings.json");

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CDS TapeLab");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
            if (d == null) return;
            AsioDriver = d.AsioDriver ?? "";
            InputStart = Math.Max(0, d.InputStart);
            OutputStart = Math.Max(0, d.OutputStart);
            SampleRate = Array.IndexOf(Rates, d.SampleRate) >= 0 ? d.SampleRate : 0;
            SweepRefDbfs = d.SweepRefDbfs is >= -24 and <= -6 ? d.SweepRefDbfs : -6;
            SaveDir = d.SaveDir ?? "";
            ReportLight = d.ReportLight;
        }
        catch { /* pokvarjena datoteka: ostanejo privzete vrednosti */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var d = new Data
            {
                AsioDriver = AsioDriver, InputStart = InputStart, OutputStart = OutputStart,
                SampleRate = SampleRate, SweepRefDbfs = SweepRefDbfs, SaveDir = SaveDir, ReportLight = ReportLight,
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
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
