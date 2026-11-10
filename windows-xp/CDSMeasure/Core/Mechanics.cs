using System;
using System.Collections.Generic;
using System.Linq;

namespace CDSMeasure.Core;

/// <summary>Vrtljivi del mehanizma, ki povzroča periodično W&amp;F motnjo pri f = v / (π·d).</summary>
public sealed record MechPart(string Sl, string En, double DiameterMm, double BeltLengthMm = 0)
{
    /// <summary>Frekvenca motnje pri dani hitrosti traku (mm/s). Pri jermenu: v_jermena / dolžina.</summary>
    public double Hz(double tapeMmS) => DiameterMm <= 0 ? 0 : tapeMmS / (Math.PI * DiameterMm);
    public string Name(bool sl) => sl ? Sl : En;
}

/// <summary>Profil mehanizma. Brez profila (Generic) program deluje splošno, brez oznak vzrokov.</summary>
public sealed class MechProfile
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public List<MechPart> Parts { get; init; } = new();
    /// <summary>Premer, po katerem jermen vrti vstrajnik, in premer motorne jermenice (Classic): motor = capstan · razmerje.</summary>
    public (double FlywheelBeltMm, double MotorPulleyMm)? MotorBelt { get; init; }
    /// <summary>Jermen med vstrajnikoma (Sankyo): frekvenca = π·d_vstrajnika·f_capstan / dolžina jermena.</summary>
    public (double FlywheelMm, double CapstanMm, double BeltLengthMm)? InterFlywheelBelt { get; init; }

    /// <summary>Modeli, ki uporabljajo ta mehanizem (prikaže se kot namig ob miški).</summary>
    public string[] Models { get; init; } = new string[0];
    /// <summary>Dragon: pogon take-up reela (motor → idler → reel). Frekvenca reela je odvisna od tuljave kasete, zato se zaenkrat ne označuje.</summary>
    public (double MotorPulleyMm, double IdlerMm, double ReelMm, double ReelWeightG)? ReelDrive { get; init; }

    public const double NominalTapeMmS = 47.625; // 1 7/8 ips

    public static readonly MechProfile Generic = new() { Id = "none", Title = "—" };

    public static readonly IList<MechProfile> All = new[]
    {
        Generic,
        new MechProfile {
            Models = new[] { "ZX-7", "RX-303", "RX-505", "480Z", "481Z", "482Z", "580M", "581", "582", "582Z", "660", "660ZX", "670", "670ZX", "680", "680ZX", "681ZX", "682ZX", "LX-3", "LX-5", "700ZXE", "700ZXL", "1000ZXL" }, Id = "classic", Title = "Nakamichi Classic",
            Parts = { new("Capstan take-up + vstrajnik (gred 2,5 mm)", "Take-up capstan + flywheel (shaft 2.5 mm)", 2.5),
                      new("Capstan supply + vstrajnik (gred 3,0 mm)", "Supply capstan + flywheel (shaft 3.0 mm)", 3.0),
                      new("Pinch roller (11 mm)", "Pinch roller (11 mm)", 11.0) },
            MotorBelt = (41.6, 7.0) },
        new MechProfile {
            Models = new[] { "BX-300", "CR-4", "CR-5", "CR-7", "CR-40", "ZX-5", "MR-1" }, Id = "sankyo", Title = "Nakamichi Sankyo DD",
            Parts = { new("Capstan take-up = motor (DD, gred 2,5 mm)", "Take-up capstan = motor (DD, shaft 2.5 mm)", 2.5),
                      new("Capstan supply (gred 2,3 mm)", "Supply capstan (shaft 2.3 mm)", 2.3),
                      new("Pinch roller (11 mm)", "Pinch roller (11 mm)", 11.0) },
            InterFlywheelBelt = (46.75, 2.5, 232.0) },
        // Sankyo BD in SD: mere vrtečih delov še niso vnesene, zato brez označb vrhov v spektru (samo modeli in tovarniški W&F).
        new MechProfile {
            Models = new[] { "CR-3", "Cassette Deck 1", "Cassette Deck 1.5", "DR-1", "DR-2", "DR-10" }, Id = "sankyobd", Title = "Nakamichi Sankyo BD" },
        new MechProfile {
            Models = new[] { "BX-1", "BX-2", "BX-100", "BX-125", "BX-150", "MR-2", "CR-1", "CR-2", "RX-202", "Cassette Deck 2", "DR-3", "DR-8" }, Id = "sankyosd", Title = "Nakamichi Sankyo SD" },
        new MechProfile {
            Models = new[] { "ZX-9" }, Id = "zx9", Title = "Nakamichi ZX-9 DD",
            Parts = { new("Capstan take-up = motor (DD, gred 2,5 mm)", "Take-up capstan = motor (DD, shaft 2.5 mm)", 2.5),
                      new("Capstan supply (gred 3,0 mm)", "Supply capstan (shaft 3.0 mm)", 3.0),
                      new("Pinch roller (11 mm)", "Pinch roller (11 mm)", 11.0) },
            InterFlywheelBelt = (48.15, 3.0, 220.0) }, // jermen po največjem premeru supply vstrajnika (48,15 mm), dolžina 220 mm
        new MechProfile {
            Models = new[] { "Dragon" }, ReelDrive = (6.0, 18.0, 25.0, 6.5), Id = "dragon", Title = "Nakamichi Dragon",
            Parts = { new("Capstan take-up = motor 1 (DD, gred 2,8 mm)", "Take-up capstan = motor 1 (DD, shaft 2.8 mm)", 2.8),
                      new("Capstan supply = motor 2 (DD, gred 3,0 mm)", "Supply capstan = motor 2 (DD, shaft 3.0 mm)", 3.0),
                      new("Pinch roller (11 mm)", "Pinch roller (11 mm)", 11.0) } },
    };

    /// <summary>Pričakovane frekvence (Hz, oznaka) pri izmerjeni hitrosti. speedRatio = izmerjena/nazivna hitrost.</summary>
    public IEnumerable<(double Hz, string Sl, string En)> Expected(double speedRatio = 1.0)
    {
        double v = NominalTapeMmS * speedRatio;
        foreach (var p in Parts) yield return (p.Hz(v), p.Sl, p.En);

        if (MotorBelt is { } mb && Parts.Count > 0)
        {
            double fCap = Parts[0].Hz(v); // take-up capstan = vstrajnik z utorom za motorni jermen
            yield return (fCap * mb.FlywheelBeltMm / mb.MotorPulleyMm, "Motor (približno, ±3 %)", "Motor (approx., ±3 %)");
        }
        if (InterFlywheelBelt is { } ib)
        {
            double fCap = new MechPart("", "", ib.CapstanMm).Hz(v);
            yield return (Math.PI * ib.FlywheelMm * fCap / ib.BeltLengthMm, "Jermen med vstrajnikoma", "Flywheel belt");
        }
    }

    /// <summary>Poišče najbližji pričakovani del za izmerjen vrh. Vrne null, če je izven tolerance (Hz).</summary>
    public (double Hz, string Sl, string En)? Match(double peakHz, double speedRatio = 1.0, double tolHz = 0.12)
    {
        (double Hz, string Sl, string En)? best = null;
        foreach (var e in Expected(speedRatio))
            if (e.Hz > 0 && (best is null || Math.Abs(e.Hz - peakHz) < Math.Abs(best.Value.Hz - peakHz))) best = e;
        return best is { } b && Math.Abs(b.Hz - peakHz) <= tolHz ? b : null;
    }
}
