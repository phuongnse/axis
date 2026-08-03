using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordPayloadHasher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Compute(IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        Dictionary<string, string[]> canonical = values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal);
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, Json));
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
