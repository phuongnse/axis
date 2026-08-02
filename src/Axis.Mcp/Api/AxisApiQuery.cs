namespace Axis.Mcp.Api;

public static class AxisApiQuery
{
    public static string Build(params (string Key, string? Value)[] values)
    {
        string query = string.Join(
            "&",
            values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        return query.Length == 0 ? string.Empty : $"?{query}";
    }
}
