using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordPayloadHasher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Result<string> Compute(IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        if (values.Any(pair => pair.Key is null || pair.Value is null))
            return Result.Failure<string>(ErrorCodes.InvalidInput, "Record values must contain non-null field keys and value arrays.");

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
