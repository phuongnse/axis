namespace Axis.Identity.Application.Services;

public static class OrganizationTimeZoneValidator
{
    public static bool IsValidIanaTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return true;

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out _);
    }
}
