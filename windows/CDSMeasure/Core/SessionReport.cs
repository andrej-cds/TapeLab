using System.Windows;
using CDSMeasure.Controls;

namespace CDSMeasure;

/// <summary>Posnetek meritve W&amp;F za skupno poročilo.</summary>
public sealed class WfSnapshot
{
    public List<Point> Hz = new(), Wrms = new(), Peak = new();
    public double RefHz = 3000, DurationMin = 45;
    public string Device = "", Date = "", Source = "", Mech = "";
    public double Limit = double.NaN;      // tovarniški maksimum (WRMS), NaN = ni znan
    public bool IsDemo;
}

/// <summary>Posnetek meritve Playback.</summary>
public sealed class PbSnapshot
{
    public List<BandChart.Series> Series = new();
    public double Top, Span;
    public string YTitle = "", Source = "";
    public bool ShowRight = true;
}

/// <summary>Posnetek meritve azimuta (zadnji veljavni odčitek in 30 s potek faze).</summary>
public sealed class AzSnapshot
{
    public double FreqHz, PhaseDeg, DelayUs, LevelL, LevelR, ZoneUs = 2, RangeDeg = 90;
    public List<Point> Trace = new();      // X = čas v sekundah, Y = faza v stopinjah
    public string Source = "";
}

/// <summary>Posnetek meritve Recording (vse izmerjene ravni).</summary>
public sealed class RecSnapshot
{
    public List<FrChart.Series> Series = new();
    public bool Auto;
    public string Source = "";
}

/// <summary>
/// Rezultati opravljenih meritev v tej seji (do zaprtja programa). Vsak pogled ob zapiranju shrani svoj posnetek,
/// skupno poročilo (PDF) pa jih zbere skupaj.
/// </summary>
public static class SessionReport
{
    public static WfSnapshot? Wf;
    public static PbSnapshot? Pb;
    public static RecSnapshot? Rec;
    public static AzSnapshot? Az;

    public static string Customer = "", Deck = "", Serial = "", Date = "", Notes = "";

    public static bool HasAny => Wf != null || Pb != null || Rec != null || Az != null;
}
