using System.Collections.Frozen;

namespace Axis.Configuration.Model;

/// <summary>
/// The ISO 4217 currencies a money field can name, with their minor units: the number of digits after
/// the decimal point of a typical amount. Funds codes are included. Codes with no minor units, such as
/// XAU, XDR and XXX, are left out, so they count as unknown.
/// </summary>
public static class Currencies
{
    private static readonly FrozenDictionary<string, int> _minorUnitsByCode = Build();

    /// <summary>
    /// Finds the minor units of <paramref name="code"/>. Matching is exact: a code is upper case.
    /// </summary>
    public static bool TryGetMinorUnits(string code, out int minorUnits) =>
        _minorUnitsByCode.TryGetValue(code, out minorUnits);

    private static FrozenDictionary<string, int> Build()
    {
        var codesByMinorUnits = new (int MinorUnits, string Codes)[]
        {
            (0, "BIF CLP DJF GNF ISK JPY KMF KRW PYG RWF UGX UYI VND VUV XAF XOF XPF"),
            (2,
                "AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BMD BND BOB BOV BRL BSD BTN BWP BYN BZD "
                + "CAD CDF CHE CHF CHW CNY COP COU CRC CUP CVE CZK DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS "
                + "GIP GMD GTQ GYD HKD HNL HTG HUF IDR ILS INR IRR JMD KES KGS KHR KPW KYD KZT LAK LBP LKR LRD LSL "
                + "MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD PAB PEN PGK "
                + "PHP PKR PLN QAR RON RSD RUB SAR SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS "
                + "TMT TOP TRY TTD TWD TZS UAH USD USN UYU UZS VED VES WST XCD XCG YER ZAR ZMW ZWG"),
            (3, "BHD IQD JOD KWD LYD OMR TND"),
            (4, "CLF UYW"),
        };

        return codesByMinorUnits
            .SelectMany(group => group.Codes.Split(' ').Select(code => (code, group.MinorUnits)))
            .ToFrozenDictionary(pair => pair.code, pair => pair.MinorUnits, StringComparer.Ordinal);
    }
}
