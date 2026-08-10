using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;

namespace Axis.Api.Solutions;

internal sealed partial class AuthorizationPolicySolutionAdapter(
    IProductPolicyInstaller installer) : ISolutionComponentAdapter
{
    public const string Type = "authorization.policy.v1";
    public string ComponentType => Type;

    public Task PreflightAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        CancellationToken cancellationToken = default)
    {
        ProductPolicyComponent policy = Parse(component, ValidationVersionId(component.Content));
        ProductPolicyInstallResult validation = installer.Validate(policy);
        if (!validation.IsInstalled)
            throw new SolutionAdapterException(
                validation.Error ?? "authorization.policy_invalid",
                retryable: false);
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        SolutionApplyReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ProductPolicyComponent policy = Parse(component, receipt.SolutionVersionId);
        ProductPolicyInstallResult result = await installer.InstallAsync(
            new InstallProductPolicyRequest(
                workspaceId,
                policy,
                receipt.SolutionVersion,
                receipt.ComponentSha256,
                receipt.OperationId.ToString("N"),
                receipt.StepId.ToString("N"),
                receipt.LeaseEpoch,
                new SubjectReference(
                    receipt.ActorSubjectKind == SolutionSubjectKind.Service
                        ? SubjectKind.Service
                        : SubjectKind.Human,
                    receipt.ActorSubjectId),
                receipt.CorrelationId),
            cancellationToken);
        if (!result.IsInstalled)
            throw new SolutionAdapterException(
                result.Error ?? "authorization.policy_install_failed",
                retryable: result.Error is "unavailable" or "conflict");
    }

    public async Task<SolutionAdapterReadback> ReadBackAsync(
        Guid workspaceId,
        SolutionAdapterPreflight component,
        SolutionApplyReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ProductPolicyComponentReadBack? readBack = await installer.ReadBackAsync(
            workspaceId,
            receipt.SolutionVersionId,
            cancellationToken);
        if (readBack is null)
            return new(false, false);
        bool matches = readBack.WorkspaceId == workspaceId
            && readBack.VersionId == receipt.SolutionVersionId
            && StringComparer.Ordinal.Equals(readBack.SolutionVersion, receipt.SolutionVersion)
            && StringComparer.Ordinal.Equals(readBack.ComponentHash, receipt.ComponentSha256)
            && StringComparer.Ordinal.Equals(readBack.OperationId, receipt.OperationId.ToString("N"))
            && StringComparer.Ordinal.Equals(readBack.StepId, receipt.StepId.ToString("N"))
            && readBack.LeaseEpoch == receipt.LeaseEpoch;
        return matches
            ? new(true, false)
            : new(false, true, "authorization.policy_readback_mismatch");
    }

    private static ProductPolicyComponent Parse(
        SolutionAdapterPreflight component,
        Guid versionId)
    {
        if (!StringComparer.Ordinal.Equals(component.Type, Type))
            throw Invalid();
        try
        {
            using JsonDocument document = CanonicalSolutionComponentJson.Parse(component.Content);
            JsonElement root = document.RootElement;
            RequireProperties(root, "schemaVersion", "policyKey", "roles", "grants");
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw Invalid();
            string policyKey = RequiredString(root, "policyKey");
            if (!ValidKey(policyKey) || !StringComparer.Ordinal.Equals(policyKey, component.Key))
                throw Invalid();

            List<ProductPolicyRole> roles = [];
            string? previousRole = null;
            foreach (JsonElement value in RequiredArray(root, "roles"))
            {
                RequireProperties(value, "key", "presentation");
                string roleKey = RequiredString(value, "key");
                if (!ValidRoleKey(roleKey) || previousRole is not null &&
                    StringComparer.Ordinal.Compare(previousRole, roleKey) >= 0)
                    throw Invalid();
                previousRole = roleKey;
                Dictionary<string, ProductRolePresentation> presentation = new(StringComparer.Ordinal);
                JsonElement entries = value.GetProperty("presentation");
                if (entries.ValueKind != JsonValueKind.Object)
                    throw Invalid();
                string? previousLanguage = null;
                foreach (JsonProperty entry in entries.EnumerateObject())
                {
                    if (!CanonicalLanguageTag(entry.Name) || previousLanguage is not null &&
                        StringComparer.Ordinal.Compare(previousLanguage, entry.Name) >= 0)
                        throw Invalid();
                    previousLanguage = entry.Name;
                    RequireProperties(entry.Value, "displayName", optionalLast: "description");
                    if (!presentation.TryAdd(
                            entry.Name,
                            new ProductRolePresentation(
                                RequiredString(entry.Value, "displayName"),
                                entry.Value.TryGetProperty("description", out JsonElement description)
                                    ? description.GetString()
                                    : null)))
                        throw Invalid();
                }
                roles.Add(new ProductPolicyRole(roleKey, presentation));
            }

            List<ProductPolicyGrant> grants = [];
            string? previousGrant = null;
            foreach (JsonElement value in RequiredArray(root, "grants"))
            {
                RequireProperties(
                    value,
                    "roleKey",
                    "actionKey",
                    "resourceType",
                    optionalLast: "resourceKey",
                    final: "scope");
                string scope = RequiredString(value, "scope");
                if (!Enum.TryParse(scope, ignoreCase: false, out ProductActionScope parsedScope)
                    || parsedScope.ToString() != scope)
                    throw Invalid();
                string roleKey = RequiredString(value, "roleKey");
                string actionKey = RequiredString(value, "actionKey");
                string resourceType = RequiredString(value, "resourceType");
                string? parsedResourceKey = value.TryGetProperty("resourceKey", out JsonElement resourceKey)
                    ? resourceKey.GetString()
                    : null;
                if (!ValidRoleKey(roleKey) || !ValidSemanticPath(actionKey) ||
                    !ValidSemanticPath(resourceType) ||
                    parsedResourceKey is not null && !ValidSemanticPath(parsedResourceKey))
                    throw Invalid();
                string identity = string.Join(
                    '\u001f',
                    roleKey,
                    actionKey,
                    resourceType,
                    parsedResourceKey ?? string.Empty,
                    parsedScope.ToString());
                if (previousGrant is not null &&
                    StringComparer.Ordinal.Compare(previousGrant, identity) >= 0)
                    throw Invalid();
                previousGrant = identity;
                grants.Add(new ProductPolicyGrant(
                    roleKey,
                    actionKey,
                    resourceType,
                    parsedResourceKey,
                    parsedScope));
            }

            return new ProductPolicyComponent(policyKey, versionId, roles, grants);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or KeyNotFoundException
            or FormatException)
        {
            throw Invalid();
        }
    }

    private static Guid ValidationVersionId(byte[] content)
    {
        byte[] digest = SHA256.HashData(content);
        Guid value = new(digest.AsSpan(0, 16));
        return value == Guid.Empty ? new Guid(1, 0, 0, new byte[8]) : value;
    }

    private static JsonElement.ArrayEnumerator RequiredArray(JsonElement value, string name)
    {
        JsonElement array = value.GetProperty(name);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            throw Invalid();
        return array.EnumerateArray();
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.GetProperty(name).ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(value.GetProperty(name).GetString())
                ? value.GetProperty(name).GetString()!
                : throw Invalid();

    private static void RequireProperties(
        JsonElement value,
        string first,
        string? second = null,
        string? third = null,
        string? fourth = null,
        string? optionalLast = null,
        string? final = null)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid();
        string[] expected = new[] { first, second, third, fourth, optionalLast, final }
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        string[] actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (optionalLast is not null && !value.TryGetProperty(optionalLast, out _))
            expected = expected.Where(name => name != optionalLast).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw Invalid();
    }

    private static SolutionAdapterException Invalid() =>
        new("authorization.policy_invalid", retryable: false);

    private static bool CanonicalLanguageTag(string value)
    {
        try
        {
            return StringComparer.Ordinal.Equals(
                value,
                CultureInfo.GetCultureInfo(value).Name);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool ValidKey(string value) => KeyPattern().IsMatch(value);

    private static bool ValidRoleKey(string value) => RoleKeyPattern().IsMatch(value);

    private static bool ValidSemanticPath(string value) =>
        value.Length <= 200 && SemanticPathPattern().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleKeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*(\\.[a-z][a-z0-9_-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticPathPattern();
}
