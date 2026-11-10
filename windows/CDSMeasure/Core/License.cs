using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CDSMeasure;

/// <summary>Licenca CDS TapeLab: podpisan ključ (ECDSA P-256), vezan na računalnik ali "friend" (brez vezave). Brez licence = demo.</summary>
public static class License
{
    // Javni ključ (zasebni je samo pri avtorju – orodje izdaj_licenco.py)
    private const string PublicKeyB64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExvnYIHm7NMwx0Bsd+oxiQrU8UxFRr98LmsdBxyBVHkMrSHX797BxSZ3vKVPwR79xbS3Yq1k7B6TNrKbMxDZS9Q==";

    public static string Name { get; private set; } = "";
    public static string Kind { get; private set; } = "";
    public static bool IsLicensed { get; private set; }

    public static string LicensePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDS TapeLab", "licenca.txt");

    public const string ServerUrl = "https://www.cassettedeck-service.com/tapelab-aktivacija.php";

    /// <summary>Vnos je lahko celoten podpisan ključ (offline / prijatelj) ali kupovalna koda XXXX-XXXX-XXXX (spletna aktivacija).</summary>
    public static async Task<string?> ActivateAsync(string input)
    {
        input = input.Trim();
        if (input.Contains('.')) return Activate(input);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var resp = await http.PostAsync(ServerUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            { ["code"] = input, ["mid"] = MachineId() }));
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            if (r.GetProperty("ok").GetBoolean()) return Activate(r.GetProperty("licence").GetString() ?? "");
            return r.GetProperty("err").GetString() switch { "used" => "lic.used", "server" => "lic.net", _ => "lic.invalid" };
        }
        catch { return "lic.net"; }
    }

    /// <summary>ID računalnika (16 znakov), ki ga stranka pošlje avtorju.</summary>
    public static string MachineId()
    {
        string guid = "";
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                     .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = k?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }
        if (guid == "") guid = Environment.MachineName;
        var h = SHA256.HashData(Encoding.UTF8.GetBytes("CDSTapeLab|" + guid));
        string hex = Convert.ToHexString(h)[..16];
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public static void Load()
    {
        IsLicensed = false; Name = ""; Kind = "";
        try { if (File.Exists(LicensePath)) Verify(File.ReadAllText(LicensePath)); } catch { }
    }

    /// <summary>Preveri ključ in ga shrani. Vrne null ob uspehu, sicer ključ sporočila v Loc.</summary>
    public static string? Activate(string key)
    {
        string? err = Verify(key);
        if (err != null) return err;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            File.WriteAllText(LicensePath, key.Trim());
        }
        catch { }
        return null;
    }

    private static string? Verify(string key)
    {
        IsLicensed = false;
        try
        {
            var parts = key.Trim().Split('.');
            if (parts.Length != 2) return "lic.bad";
            byte[] payload = FromB64(parts[0]), sig = FromB64(parts[1]);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyB64), out _);
            if (!ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return "lic.bad";
            using var doc = JsonDocument.Parse(payload);
            var r = doc.RootElement;
            string m = r.GetProperty("m").GetString() ?? "";
            if (m != "*" && !string.Equals(m, MachineId(), StringComparison.OrdinalIgnoreCase)) return "lic.machine";
            Name = r.GetProperty("n").GetString() ?? "";
            Kind = r.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";
            IsLicensed = true;
            return null;
        }
        catch { return "lic.bad"; }
    }

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
