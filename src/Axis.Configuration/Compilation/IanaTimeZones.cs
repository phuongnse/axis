namespace Axis.Configuration.Compilation;

/// <summary>The one rule for which time zone ids a configuration may name, shared by the application manifest and schedule triggers.</summary>
internal static class IanaTimeZones
{
    /// <summary>Finds an IANA time zone by its exact id. A Windows id, or an id in another letter case, is not found.</summary>
    public static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found)
            && found.HasIanaId
            && string.Equals(found.Id, id, StringComparison.Ordinal))
        {
            timeZone = found;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}
