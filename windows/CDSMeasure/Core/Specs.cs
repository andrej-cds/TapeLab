using System;
using System.Collections.Generic;
using System.Linq;

namespace CDSMeasure.Core;

/// <summary>Tovarniška specifikacija W&amp;F za model (iz tabele Nakamichi_WF_specifikacije.xlsx).</summary>
public sealed record ModelSpec(string MechId, string Model, double WfMax, string Standard);

public static class Specs
{
    public static readonly ModelSpec[] All =
    {
        new("classic", "ZX-7", 0.040, "WRMS"),
        new("classic", "RX-303", 0.040, "WRMS"),
        new("classic", "RX-505", 0.040, "WRMS"),
        new("classic", "480Z", 0.040, "WRMS"),
        new("classic", "481Z", 0.040, "WRMS"),
        new("classic", "482Z", 0.040, "WRMS"),
        new("classic", "580M", 0.040, "WRMS"),
        new("classic", "581", 0.040, "WRMS"),
        new("classic", "582", 0.040, "WRMS"),
        new("classic", "582Z", 0.040, "WRMS"),
        new("classic", "660", 0.040, "WRMS"),
        new("classic", "660ZX", 0.040, "WRMS"),
        new("classic", "670", 0.040, "WRMS"),
        new("classic", "670ZX", 0.040, "WRMS"),
        new("classic", "680", 0.040, "WRMS"),
        new("classic", "680ZX", 0.040, "WRMS"),
        new("classic", "681ZX", 0.040, "WRMS"),
        new("classic", "682ZX", 0.040, "WRMS"),
        new("classic", "LX-3", 0.040, "WRMS"),
        new("classic", "LX-5", 0.040, "WRMS"),
        new("classic", "700ZXE", 0.040, "WRMS"),
        new("classic", "700ZXL", 0.040, "WRMS"),
        new("classic", "1000ZXL", 0.040, "WRMS"),
        new("zx9", "ZX-9", 0.022, "WRMS"),
        new("sankyosd", "BX-1", 0.060, "WRMS"),
        new("sankyosd", "BX-2", 0.060, "WRMS"),
        new("sankyosd", "BX-100", 0.060, "WRMS"),
        new("sankyosd", "BX-125", 0.060, "WRMS"),
        new("sankyosd", "BX-150", 0.060, "WRMS"),
        new("sankyo", "BX-300", 0.027, "WRMS"),
        new("sankyosd", "CR-1", 0.060, "WRMS"),
        new("sankyosd", "CR-2", 0.060, "WRMS"),
        new("sankyobd", "CR-3", 0.035, "WRMS"),
        new("sankyo", "CR-4", 0.035, "WRMS"),
        new("sankyo", "CR-5", 0.027, "WRMS"),
        new("sankyo", "CR-7", 0.027, "WRMS"),
        new("sankyo", "CR-40", 0.035, "WRMS"),
        new("sankyo", "MR-1", 0.027, "WRMS"),
        new("sankyosd", "MR-2", 0.060, "WRMS"),
        new("sankyosd", "RX-202", 0.060, "WRMS"),
        new("sankyobd", "Cassette Deck 1", 0.035, "WRMS"),
        new("sankyobd", "Cassette Deck 1.5", 0.035, "WRMS"),
        new("sankyosd", "Cassette Deck 2", 0.060, "WRMS"),
        new("sankyo", "ZX-5", 0.027, "WRMS"),
        new("sankyobd", "DR-1", 0.035, "WRMS"),
        new("sankyobd", "DR-2", 0.035, "WRMS"),
        new("sankyosd", "DR-3", 0.060, "WRMS"),
        new("sankyosd", "DR-8", 0.060, "WRMS"),
        new("sankyobd", "DR-10", 0.035, "WRMS"),
        new("dragon", "Dragon", 0.019, "WRMS"),
    };

    public static IEnumerable<ModelSpec> For(string mechId) => All.Where(s => s.MechId == mechId);
}
