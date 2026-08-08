using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.Solutions.Application;

public sealed partial class SolutionPackageVerifier(ITrustedPublisherKeyReader trustedKeys)
{
    public const string PayloadType = "application/vnd.axis.solution.v1+json";
    public const int MaximumEnvelopeBytes = 10 * 1024 * 1024;
    public const int MaximumComponentBytes = 1024 * 1024;

    private static readonly string[] RootProperties =
    [
        "schemaVersion", "solutionKey", "solutionVersion", "axisOpenApiSha256", "publisher", "provenance", "components",
    ];
    private static readonly string[] PublisherProperties = ["publisherId", "publisherKeyId"];
    private static readonly string[] ProvenanceProperties = ["sourceRevision", "buildId", "builtAt", "sourceUri"];
    private static readonly string[] ComponentProperties = ["type", "key", "sha256", "content", "dependsOn"];
    private static readonly string[] DependencyProperties = ["type", "key"];
    private static readonly HashSet<string> SupportedTypes =
    [
        "authorization.policy.v1",
        "business-object.definition.v1",
        "rule.binding.v1",
    ];

    public async Task<VerifiedSolutionPackage> VerifyAsync(
        ReadOnlyMemory<byte> envelopeBytes,
        string currentAxisOpenApiSha256,
        CancellationToken cancellationToken = default)
    {
        if (envelopeBytes.IsEmpty || envelopeBytes.Length > MaximumEnvelopeBytes)
            Fail("solutions.package.size_invalid");
        RequireSha256(currentAxisOpenApiSha256, "solutions.package.axis_openapi_invalid");

        using JsonDocument envelope = ParseJson(envelopeBytes.Span, canonical: false);
        JsonElement root = RequireObject(envelope.RootElement, "solutions.package.envelope_invalid");
        RejectDuplicateProperties(root, recurse: false);
        string payloadType = RequiredString(root, "payloadType");
        if (!string.Equals(payloadType, PayloadType, StringComparison.Ordinal))
            Fail("solutions.package.payload_type_invalid");
        byte[] payloadBytes = DecodeEnvelopeBase64(RequiredString(root, "payload"));

        JsonElement signatures = RequiredProperty(root, "signatures");
        if (signatures.ValueKind != JsonValueKind.Array || signatures.GetArrayLength() != 1)
            Fail("solutions.package.signature_count_invalid");
        JsonElement signature = RequireObject(signatures[0], "solutions.package.signature_invalid");
        RejectDuplicateProperties(signature, recurse: false);
        byte[] signatureBytes = DecodeEnvelopeBase64(RequiredString(signature, "sig"));
        if (signatureBytes.Length != 64)
            Fail("solutions.package.signature_invalid");

        ParsedPayload payload = ParsePayload(payloadBytes, currentAxisOpenApiSha256);
        TrustedPublisherSnapshot? trustedKey = await trustedKeys.FindAsync(
            payload.PublisherId,
            payload.PublisherKeyId,
            cancellationToken);
        if (trustedKey is null || !trustedKey.IsActive || trustedKey.IsTombstone)
            Fail("solutions.package.publisher_untrusted");

        byte[] pae = CreatePae(PayloadType, payloadBytes);
        using ECDsa key = ECDsa.Create();
        try
        {
            key.ImportFromPem(trustedKey.PublicKeyPem);
            ECParameters parameters = key.ExportParameters(false);
            if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value ||
                !key.VerifyData(pae, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                Fail("solutions.package.signature_invalid");
            }
        }
        catch (CryptographicException)
        {
            Fail("solutions.package.signature_invalid");
        }

        string packageHash = Convert.ToHexString(SHA256.HashData(envelopeBytes.Span)).ToLowerInvariant();
        return new VerifiedSolutionPackage(
            envelopeBytes.ToArray(),
            payloadBytes,
            packageHash,
            payload.SolutionKey,
            payload.SolutionVersion,
            payload.AxisOpenApiSha256,
            payload.PublisherId,
            payload.PublisherKeyId,
            payload.Provenance,
            payload.Components);
    }

    public static byte[] CreatePae(string payloadType, ReadOnlySpan<byte> payload)
    {
        byte[] type = Encoding.UTF8.GetBytes(payloadType);
        byte[] prefix = Encoding.ASCII.GetBytes($"DSSEv1 {type.Length} ");
        byte[] separator = Encoding.ASCII.GetBytes($" {payload.Length} ");
        byte[] result = new byte[prefix.Length + type.Length + separator.Length + payload.Length];
        int offset = 0;
        prefix.CopyTo(result, offset);
        offset += prefix.Length;
        type.CopyTo(result, offset);
        offset += type.Length;
        separator.CopyTo(result, offset);
        offset += separator.Length;
        payload.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static ParsedPayload ParsePayload(byte[] payloadBytes, string currentAxisOpenApiSha256)
    {
        using JsonDocument document = ParseJson(payloadBytes, canonical: true);
        JsonElement root = RequireObject(document.RootElement, "solutions.package.payload_invalid");
        RequireExactProperties(root, RootProperties, "solutions.package.payload_invalid");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number ||
            root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            Fail("solutions.package.schema_version_invalid");
        }

        string solutionKey = RequireMatch(root.GetProperty("solutionKey").GetString(), KeyRegex(), "solutions.package.solution_key_invalid");
        string solutionVersion = RequireMatch(root.GetProperty("solutionVersion").GetString(), SemVerRegex(), "solutions.package.version_invalid");
        string axisOpenApiSha256 = root.GetProperty("axisOpenApiSha256").GetString() ?? string.Empty;
        RequireSha256(axisOpenApiSha256, "solutions.package.axis_openapi_invalid");
        if (!string.Equals(axisOpenApiSha256, currentAxisOpenApiSha256, StringComparison.Ordinal))
            Fail("solutions.package.axis_openapi_mismatch");

        JsonElement publisher = RequireObject(root.GetProperty("publisher"), "solutions.package.publisher_invalid");
        RequireExactProperties(publisher, PublisherProperties, "solutions.package.publisher_invalid");
        string publisherId = RequireMatch(publisher.GetProperty("publisherId").GetString(), KeyRegex(), "solutions.package.publisher_invalid");
        string publisherKeyId = RequireMatch(publisher.GetProperty("publisherKeyId").GetString(), KeyRegex(), "solutions.package.publisher_invalid");

        JsonElement provenance = RequireObject(root.GetProperty("provenance"), "solutions.package.provenance_invalid");
        RequireExactProperties(provenance, ProvenanceProperties, "solutions.package.provenance_invalid");
        string sourceRevision = RequireMatch(provenance.GetProperty("sourceRevision").GetString(), GitRevisionRegex(), "solutions.package.provenance_invalid");
        string buildId = RequireBoundedString(provenance.GetProperty("buildId"), 1, 128, "solutions.package.provenance_invalid");
        if (!DateTimeOffset.TryParseExact(
                provenance.GetProperty("builtAt").GetString(),
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset builtAt))
        {
            Fail("solutions.package.provenance_invalid");
        }
        if (!Uri.TryCreate(provenance.GetProperty("sourceUri").GetString(), UriKind.Absolute, out Uri? sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps || sourceUri.AbsoluteUri.Length > 2048)
        {
            Fail("solutions.package.provenance_invalid");
        }

        IReadOnlyList<VerifiedSolutionComponent> components = ParseComponents(root.GetProperty("components"));
        return new ParsedPayload(
            solutionKey,
            solutionVersion,
            axisOpenApiSha256,
            publisherId,
            publisherKeyId,
            new SolutionProvenance(sourceRevision, buildId, builtAt, sourceUri!),
            components);
    }

    private static IReadOnlyList<VerifiedSolutionComponent> ParseComponents(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 256)
            Fail("solutions.package.components_invalid");

        List<VerifiedSolutionComponent> components = [];
        (string Type, string Key)? previous = null;
        int edges = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            JsonElement component = RequireObject(item, "solutions.package.component_invalid");
            RequireExactProperties(component, ComponentProperties, "solutions.package.component_invalid");
            string type = RequireMatch(component.GetProperty("type").GetString(), TypeIdRegex(), "solutions.package.component_invalid");
            string key = RequireMatch(component.GetProperty("key").GetString(), ComponentKeyRegex(), "solutions.package.component_invalid");
            if (!SupportedTypes.Contains(type))
                Fail("solutions.package.component_type_invalid");
            if (previous is { } prior && Compare((type, key), prior) <= 0)
                Fail("solutions.package.component_order_invalid");
            previous = (type, key);

            string sha256 = component.GetProperty("sha256").GetString() ?? string.Empty;
            RequireSha256(sha256, "solutions.package.component_hash_invalid");
            byte[] content = DecodeComponentBase64Url(component.GetProperty("content").GetString() ?? string.Empty);
            if (content.Length > MaximumComponentBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), sha256, StringComparison.Ordinal))
            {
                Fail("solutions.package.component_hash_invalid");
            }

            JsonElement dependencyArray = component.GetProperty("dependsOn");
            if (dependencyArray.ValueKind != JsonValueKind.Array)
                Fail("solutions.package.dependencies_invalid");
            List<SolutionComponentReference> dependencies = [];
            (string Type, string Key)? priorDependency = null;
            foreach (JsonElement dependencyValue in dependencyArray.EnumerateArray())
            {
                JsonElement dependency = RequireObject(dependencyValue, "solutions.package.dependencies_invalid");
                RequireExactProperties(dependency, DependencyProperties, "solutions.package.dependencies_invalid");
                string dependencyType = RequireMatch(dependency.GetProperty("type").GetString(), TypeIdRegex(), "solutions.package.dependencies_invalid");
                string dependencyKey = RequireMatch(dependency.GetProperty("key").GetString(), ComponentKeyRegex(), "solutions.package.dependencies_invalid");
                if (priorDependency is { } priorItem && Compare((dependencyType, dependencyKey), priorItem) <= 0)
                    Fail("solutions.package.dependencies_invalid");
                if (dependencyType == type && dependencyKey == key)
                    Fail("solutions.package.dependencies_invalid");
                priorDependency = (dependencyType, dependencyKey);
                dependencies.Add(new SolutionComponentReference(dependencyType, dependencyKey));
                edges++;
            }
            components.Add(new VerifiedSolutionComponent(type, key, sha256, content, dependencies));
        }

        if (edges > 512)
            Fail("solutions.package.dependencies_invalid");
        ValidateGraph(components);
        return components;
    }

    private static void ValidateGraph(IReadOnlyList<VerifiedSolutionComponent> components)
    {
        Dictionary<(string, string), VerifiedSolutionComponent> byIdentity = components.ToDictionary(item => (item.Type, item.Key));
        Dictionary<(string, string), int> depths = [];
        HashSet<(string, string)> visiting = [];

        int Visit((string, string) identity)
        {
            if (depths.TryGetValue(identity, out int known))
                return known;
            if (!byIdentity.TryGetValue(identity, out VerifiedSolutionComponent? component) || !visiting.Add(identity))
                Fail("solutions.package.dependencies_invalid");
            int depth = component.DependsOn.Count == 0
                ? 1
                : 1 + component.DependsOn.Max(dependency => Visit((dependency.Type, dependency.Key)));
            visiting.Remove(identity);
            if (depth > 32)
                Fail("solutions.package.dependencies_invalid");
            depths[identity] = depth;
            return depth;
        }

        foreach ((string type, string key) in byIdentity.Keys)
            Visit((type, key));
    }

    private static JsonDocument ParseJson(ReadOnlySpan<byte> bytes, bool canonical)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            if (canonical)
                RejectDuplicateProperties(document.RootElement, recurse: true);
            if (canonical)
            {
                ValidateCanonicalValues(document.RootElement);
                byte[] encoded = EncodeCanonical(document.RootElement);
                if (!bytes.SequenceEqual(encoded))
                {
                    document.Dispose();
                    Fail("solutions.package.canonical_json_invalid");
                }
            }
            return document;
        }
        catch (JsonException)
        {
            Fail(canonical ? "solutions.package.canonical_json_invalid" : "solutions.package.envelope_invalid");
            throw;
        }
    }

    private static void ValidateCanonicalValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ValidateString(property.Name);
                    ValidateCanonicalValues(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    ValidateCanonicalValues(item);
                break;
            case JsonValueKind.String:
                ValidateString(element.GetString()!);
                break;
            case JsonValueKind.Number:
                if (!IntegerRegex().IsMatch(element.GetRawText()))
                    Fail("solutions.package.canonical_json_invalid");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                Fail("solutions.package.canonical_json_invalid");
                break;
        }
    }

    private static void ValidateString(string value)
    {
        if (value != value.Trim() || !value.IsNormalized(NormalizationForm.FormC))
            Fail("solutions.package.canonical_json_invalid");
    }

    private static byte[] EncodeCanonical(JsonElement element)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        });
        WriteCanonical(writer, element);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                Fail("solutions.package.canonical_json_invalid");
                break;
        }
    }

    private static void RejectDuplicateProperties(JsonElement element, bool recurse)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    Fail("solutions.package.duplicate_property");
                if (recurse)
                    RejectDuplicateProperties(property.Value, recurse: true);
            }
        }
        else if (recurse && element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                RejectDuplicateProperties(item, recurse: true);
        }
    }

    private static byte[] DecodeEnvelopeBase64(string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || encoded.Any(char.IsWhiteSpace))
            Fail("solutions.package.base64_invalid");
        bool standard = encoded.IndexOfAny(['+', '/']) >= 0;
        bool url = encoded.IndexOfAny(['-', '_']) >= 0;
        if (standard && url)
            Fail("solutions.package.base64_invalid");
        string normalized = url ? encoded.Replace('-', '+').Replace('_', '/') : encoded;
        int firstPadding = normalized.IndexOf('=');
        if (firstPadding >= 0 && normalized[firstPadding..].Any(character => character != '=') || normalized.Count(character => character == '=') > 2)
            Fail("solutions.package.base64_invalid");
        int remainder = normalized.Length % 4;
        if (remainder == 1 || firstPadding >= 0 && remainder != 0)
            Fail("solutions.package.base64_invalid");
        if (firstPadding < 0 && remainder > 0)
            normalized = normalized.PadRight(normalized.Length + 4 - remainder, '=');
        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            Fail("solutions.package.base64_invalid");
            throw;
        }
    }

    private static byte[] DecodeComponentBase64Url(string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || encoded.Contains('=') || encoded.Any(character =>
                !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            Fail("solutions.package.component_content_invalid");
        }
        int remainder = encoded.Length % 4;
        if (remainder == 1)
            Fail("solutions.package.component_content_invalid");
        string normalized = encoded.Replace('-', '+').Replace('_', '/');
        if (remainder > 0)
            normalized = normalized.PadRight(normalized.Length + 4 - remainder, '=');
        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            Fail("solutions.package.component_content_invalid");
            throw;
        }
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            Fail("solutions.package.envelope_invalid");
        return value;
    }

    private static string RequiredString(JsonElement element, string name)
    {
        JsonElement value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            Fail("solutions.package.envelope_invalid");
        return value.GetString()!;
    }

    private static JsonElement RequireObject(JsonElement element, string problemCode)
    {
        if (element.ValueKind != JsonValueKind.Object)
            Fail(problemCode);
        return element;
    }

    private static void RequireExactProperties(JsonElement element, IReadOnlyList<string> expected, string problemCode)
    {
        string[] actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            Fail(problemCode);
    }

    private static string RequireBoundedString(JsonElement element, int minimum, int maximum, string problemCode)
    {
        if (element.ValueKind != JsonValueKind.String)
            Fail(problemCode);
        string value = element.GetString()!;
        if (value.Length < minimum || value.Length > maximum)
            Fail(problemCode);
        return value;
    }

    private static string RequireMatch(string? value, Regex regex, string problemCode)
    {
        if (value is null || !regex.IsMatch(value))
            Fail(problemCode);
        return value;
    }

    private static void RequireSha256(string value, string problemCode)
    {
        if (!Sha256Regex().IsMatch(value))
            Fail(problemCode);
    }

    private static int Compare((string Type, string Key) left, (string Type, string Key) right)
    {
        int type = string.CompareOrdinal(left.Type, right.Type);
        return type != 0 ? type : string.CompareOrdinal(left.Key, right.Key);
    }

    [DoesNotReturn]
    private static void Fail(string problemCode) => throw new SolutionPackageException(problemCode);

    [GeneratedRegex("\\A[a-z][a-z0-9_]{0,62}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    [GeneratedRegex("\\A[a-z][a-z0-9_.:@-]{0,199}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentKeyRegex();

    [GeneratedRegex("\\A[a-z][a-z0-9_.-]{0,127}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypeIdRegex();

    [GeneratedRegex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("\\A(?:[0-9a-f]{40}|[0-9a-f]{64})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex GitRevisionRegex();

    [GeneratedRegex("\\A(?:0|-?[1-9][0-9]*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex("\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-(?:(?:0|[1-9][0-9]*)|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:(?:0|[1-9][0-9]*)|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SemVerRegex();

    private sealed record ParsedPayload(
        string SolutionKey,
        string SolutionVersion,
        string AxisOpenApiSha256,
        string PublisherId,
        string PublisherKeyId,
        SolutionProvenance Provenance,
        IReadOnlyList<VerifiedSolutionComponent> Components);
}
