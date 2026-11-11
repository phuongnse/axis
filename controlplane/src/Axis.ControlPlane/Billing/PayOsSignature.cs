using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// The checksum of PayOS, see <c>docs/reference/payments.md#paying-in-vietnam</c>: the HMAC-SHA256 of a request's fields, and of the <c>data</c> of an answer or a webhook,
/// made with the checksum key and built as the PayOS SDK builds it. One class holds the rules, so the check of an answer and the check of a webhook cannot drift apart.
/// </summary>
internal static class PayOsSignature
{
    private static readonly JsonWriterOptions _relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The lowercase hex HMAC-SHA256 of <paramref name="text"/>.</summary>
    public static string Sign(byte[] key, string text) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)));

    /// <summary>Whether <paramref name="signature"/> is the checksum of <paramref name="data"/>. The comparison takes the same time whatever the signature holds.</summary>
    public static bool IsSigned(byte[] key, JsonElement data, string signature) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(key, Canonical(data))),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

    // The form that PayOS's SDK signs (sortObjDataByKey and convertObjToQueryStr): the keys in ordinal order as key=value joined by &. A null, "null" or "undefined" is empty,
    // a string is as it is, a number or a bool is its JSON text, and an array is compact JSON with the keys of each of its objects in ordinal order.
    private static string Canonical(JsonElement data)
    {
        var parts = new List<string>();
        foreach (var property in data.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Array => SortedArray(property.Value),
                _ => property.Value.GetRawText(),
            };
            if (value is "null" or "undefined")
            {
                value = string.Empty;
            }

            parts.Add($"{property.Name}={value}");
        }

        return string.Join('&', parts);
    }

    private static string SortedArray(JsonElement array)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _relaxed))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    item.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (var property in item.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
