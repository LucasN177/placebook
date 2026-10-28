using System.Globalization;

namespace Placebook.Models;

public static class Countries
{
    // Feste deutsche Namen: RegionInfo.DisplayName liefert in Blazor WebAssembly nur den Namen in Landessprache
    // ("España", "Italia"), unabhängig von der eingestellten Kultur.
    private static readonly Dictionary<string, string> German = new()
    {
        ["AD"] = "Andorra", ["AL"] = "Albanien", ["AT"] = "Österreich", ["BA"] = "Bosnien und Herzegowina",
        ["BE"] = "Belgien", ["BG"] = "Bulgarien", ["BY"] = "Belarus", ["CH"] = "Schweiz", ["CY"] = "Zypern",
        ["CZ"] = "Tschechien", ["DE"] = "Deutschland", ["DK"] = "Dänemark", ["EE"] = "Estland", ["ES"] = "Spanien",
        ["FI"] = "Finnland", ["FR"] = "Frankreich", ["GB"] = "Vereinigtes Königreich", ["GR"] = "Griechenland",
        ["HR"] = "Kroatien", ["HU"] = "Ungarn", ["IE"] = "Irland", ["IS"] = "Island", ["IT"] = "Italien",
        ["LI"] = "Liechtenstein", ["LT"] = "Litauen", ["LU"] = "Luxemburg", ["LV"] = "Lettland", ["MC"] = "Monaco",
        ["MD"] = "Moldau", ["ME"] = "Montenegro", ["MK"] = "Nordmazedonien", ["MT"] = "Malta",
        ["NL"] = "Niederlande", ["NO"] = "Norwegen", ["PL"] = "Polen", ["PT"] = "Portugal", ["RO"] = "Rumänien",
        ["RS"] = "Serbien", ["SE"] = "Schweden", ["SI"] = "Slowenien", ["SK"] = "Slowakei", ["SM"] = "San Marino",
        ["UA"] = "Ukraine", ["VA"] = "Vatikanstadt", ["XK"] = "Kosovo"
    };

    /// <summary>Deutscher Ländername, z. B. „Österreich“ für <c>AT</c>.</summary>
    public static string Name(string countryCode)
    {
        if (German.TryGetValue(countryCode, out var name)) return name;
        try
        {
            return new RegionInfo(countryCode).NativeName;
        }
        catch (ArgumentException)
        {
            return countryCode;
        }
    }
}
