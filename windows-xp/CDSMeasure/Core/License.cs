using System;
using System.IO;

namespace CDSMeasure;

/// <summary>
/// XP izdaja (testna): licenčni sistem je izpuščen, ker .NET 4.0 na Windows XP nima ECDSA in sodobnega TLS (spletna aktivacija ne bi delovala).
/// Program deluje v celoti, kot da je licenciran.
/// </summary>
public static class License
{
    public static string Name { get; private set; } = "XP test";
    public static string Kind { get; private set; } = "xp";
    public static bool IsLicensed { get; private set; } = true;

    public static string LicensePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDS TapeLab", "licenca.txt");

    public const string ServerUrl = "";

    public static string MachineId() => "XP-TEST";

    public static void Load() { IsLicensed = true; Name = "XP test"; Kind = "xp"; }

    public static string? Activate(string key) => null;
}
