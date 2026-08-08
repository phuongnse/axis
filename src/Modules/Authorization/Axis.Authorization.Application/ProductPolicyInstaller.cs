using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Contracts;
using Domain = Axis.Authorization.Domain;

namespace Axis.Authorization.Application;

public sealed record StoredProductPolicy(
    Guid WorkspaceId,
    ProductPolicyComponent Component,
    string CanonicalContent,
    string Provenance,
    DateTimeOffset InstalledAt);

public interface IInstalledProductPolicyStore
{
    Task<StoredProductPolicy?> GetAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken = default);
    Task AddAsync(StoredProductPolicy policy, CancellationToken cancellationToken = default);
    Task<bool> TryUpdateProvenanceAsync(
        Guid workspaceId,
        Guid versionId,
        string expectedProvenance,
        string newProvenance,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredProductPolicy>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredProductPolicy>>([]);
}

public sealed class ProductPolicyInstaller(
    IInstalledProductPolicyStore store,
    IProductActionDescriptorRegistry descriptors,
    IAuthorizationAuditSink audit,
    IAuthorizationUnitOfWork unitOfWork,
    TimeProvider clock) : IProductPolicyInstaller
{
    private sealed record StoredProvenance(
        string SolutionVersion,
        string ComponentHash,
        string Operation,
        string Step,
        long LeaseEpoch);

    public ProductPolicyInstallResult Validate(ProductPolicyComponent component)
    {
        if (component is null)
            return new(false, "authorization.policy_invalid");

        IReadOnlyList<ProductActionDescriptor> registered = component.Grants
            .Select(grant => descriptors.Find(grant.ActionKey, grant.ResourceType))
            .OfType<ProductActionDescriptor>()
            .DistinctBy(descriptor => (descriptor.ActionKey, descriptor.ResourceType))
            .ToArray();
        return registered.Count == component.Grants
                .Select(grant => (grant.ActionKey, grant.ResourceType))
                .Distinct()
                .Count()
            && Domain.ProductPolicyValidation.Validate(
                ToDomain(component),
                registered.Select(ToDomain).ToArray()) is null
                ? new(true)
                : new(false, "authorization.policy_invalid");
    }

    public async Task<ProductPolicyInstallResult> InstallAsync(
        InstallProductPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidRequest(request))
            return new(false, "authorization.policy_invalid");

        ProductPolicyInstallResult validation = Validate(request.Component);
        if (!validation.IsInstalled)
            return validation;

        string canonicalContent = CanonicalContent(request.Component);
        string provenance = CanonicalProvenance(request);
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            StoredProductPolicy? existing = await store.GetAsync(
                request.WorkspaceId,
                request.Component.VersionId,
                cancellationToken);
            if (existing is not null)
            {
                string existingCanonicalContent = CanonicalContent(existing.Component);
                if (!StringComparer.Ordinal.Equals(existing.CanonicalContent, existingCanonicalContent) ||
                    !StringComparer.Ordinal.Equals(existingCanonicalContent, canonicalContent))
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return new(false, "authorization.policy_immutable");
                }

                if (!TryReadCanonicalProvenance(existing.Provenance, out StoredProvenance? storedProvenance) ||
                    storedProvenance is null ||
                    !SameReceiptIdentity(request, storedProvenance))
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return new(false, "authorization.policy_receipt_conflict");
                }

                if (request.LeaseEpoch < storedProvenance.LeaseEpoch)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return new(false, "authorization.policy_stale_receipt");
                }

                if (request.LeaseEpoch == storedProvenance.LeaseEpoch)
                {
                    AuditEventV1 originalEvent = AuditEvent(
                        request,
                        canonicalContent,
                        existing.Provenance,
                        existing.InstalledAt);
                    AuditEventReadBackV1? originalAudit = await audit.ReadBackAsync(
                        originalEvent.EventId,
                        cancellationToken);
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return ReceiptAuditMatches(originalEvent, originalAudit)
                        ? new(true)
                        : new(false, "audit_unavailable");
                }

                return await AdvanceReceiptAsync(
                    request,
                    existing,
                    canonicalContent,
                    provenance,
                    cancellationToken);
            }

            DateTimeOffset now = clock.GetUtcNow();
            AuditEventV1 auditEvent = AuditEvent(request, canonicalContent, provenance, now);
            AuditIngestionResult staged = await audit.IngestAsync(auditEvent, cancellationToken);
            if (staged.Disposition is AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, "audit_unavailable");
            }

            await store.AddAsync(new(
                request.WorkspaceId,
                request.Component,
                canonicalContent,
                provenance,
                now), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            AuditEventReadBackV1? readBack = await audit.ReadBackAsync(auditEvent.EventId, cancellationToken);
            if (!ReceiptAuditMatches(auditEvent, readBack))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, "audit_unavailable");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new(true);
        }
        catch (AuthorizationPersistenceConflictException)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, "conflict");
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, "unavailable");
        }
    }

    public async Task<ProductPolicyComponentReadBack?> ReadBackAsync(
        Guid workspaceId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        StoredProductPolicy? stored = await store.GetAsync(
            workspaceId,
            versionId,
            cancellationToken);
        if (stored is null)
            return null;

        if (!StringComparer.Ordinal.Equals(stored.CanonicalContent, CanonicalContent(stored.Component)) ||
            !TryReadCanonicalProvenance(stored.Provenance, out StoredProvenance? provenance) ||
            provenance is null)
            return null;

        return new(
            stored.WorkspaceId,
            stored.Component.VersionId,
            provenance.SolutionVersion,
            provenance.ComponentHash,
            provenance.Operation,
            provenance.Step,
            provenance.LeaseEpoch);
    }

    private async Task<ProductPolicyInstallResult> AdvanceReceiptAsync(
        InstallProductPolicyRequest request,
        StoredProductPolicy existing,
        string canonicalContent,
        string provenance,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        AuditEventV1 auditEvent = AuditEvent(request, canonicalContent, provenance, now);
        AuditIngestionResult staged = await audit.IngestAsync(auditEvent, cancellationToken);
        if (staged.Disposition is AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, "audit_unavailable");
        }

        bool advanced = await store.TryUpdateProvenanceAsync(
            request.WorkspaceId,
            request.Component.VersionId,
            existing.Provenance,
            provenance,
            cancellationToken);
        if (!advanced)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, "conflict");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        AuditEventReadBackV1? readBack = await audit.ReadBackAsync(
            auditEvent.EventId,
            cancellationToken);
        if (!ReceiptAuditMatches(auditEvent, readBack))
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, "audit_unavailable");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return new(true);
    }

    private static bool ValidRequest(InstallProductPolicyRequest request) =>
        request.WorkspaceId != Guid.Empty && request.Component.VersionId != Guid.Empty &&
        IsText(request.SolutionVersion, 200) && IsSha256(request.ComponentHash) &&
        IsText(request.OperationId, 120) && IsText(request.StepId, 120) && request.LeaseEpoch > 0 &&
        request.OriginatingSubject.Id != Guid.Empty && Enum.IsDefined(request.OriginatingSubject.Kind) &&
        IsText(request.CorrelationId, AuditEventV1Validator.MaximumCorrelationIdLength);

    private static bool IsText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character)) &&
        StringComparer.Ordinal.Equals(value, value.ToLowerInvariant());

    private static AuditEventV1 AuditEvent(
        InstallProductPolicyRequest request,
        string canonicalContent,
        string canonicalProvenance,
        DateTimeOffset occurredAt)
    {
        return new(
            EventId(request.WorkspaceId, canonicalContent, canonicalProvenance),
            AuditActorKindV1.System,
            null,
            request.OriginatingSubject.Id,
            request.WorkspaceId,
            "authorization.policy_install",
            "product-policy",
            request.Component.VersionId,
            "installed",
            occurredAt,
            request.CorrelationId,
            new Dictionary<string, string>
            {
                ["policy"] = request.Component.PolicyKey,
                ["component_hash"] = request.ComponentHash,
                ["solution_version"] = request.SolutionVersion,
                ["step"] = request.StepId,
                ["lease_epoch"] = request.LeaseEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["originating_subject_kind"] = request.OriginatingSubject.Kind.ToString(),
            });
    }

    private static Guid EventId(
        Guid workspaceId,
        string canonicalContent,
        string canonicalProvenance)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{workspaceId:N}\u001f{canonicalContent}\u001f{canonicalProvenance}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static bool ReceiptAuditMatches(
        AuditEventV1 expected,
        AuditEventReadBackV1? actual) =>
        actual is not null && AuditEventV1ReadBack.Matches(expected, actual);

    private static string CanonicalContent(ProductPolicyComponent component)
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        writer.WriteString("policyKey", component.PolicyKey);
        writer.WriteString("versionId", component.VersionId);
        writer.WritePropertyName("roles");
        writer.WriteStartArray();
        foreach (ProductPolicyRole role in component.Roles.OrderBy(value => value.RoleKey, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("roleKey", role.RoleKey);
            writer.WritePropertyName("presentation");
            writer.WriteStartObject();
            foreach ((string language, ProductRolePresentation presentation) in role.Presentation.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(language);
                writer.WriteStartObject();
                writer.WriteString("displayName", presentation.DisplayName);
                if (presentation.Description is not null)
                    writer.WriteString("description", presentation.Description);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("grants");
        writer.WriteStartArray();
        foreach (ProductPolicyGrant grant in component.Grants.OrderBy(value => value.RoleKey, StringComparer.Ordinal)
                     .ThenBy(value => value.ActionKey, StringComparer.Ordinal)
                     .ThenBy(value => value.ResourceType, StringComparer.Ordinal)
                     .ThenBy(value => value.ResourceKey ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(value => value.Scope))
        {
            writer.WriteStartObject();
            writer.WriteString("roleKey", grant.RoleKey);
            writer.WriteString("actionKey", grant.ActionKey);
            writer.WriteString("resourceType", grant.ResourceType);
            if (grant.ResourceKey is not null)
                writer.WriteString("resourceKey", grant.ResourceKey);
            writer.WriteString("scope", grant.Scope.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static string CanonicalProvenance(InstallProductPolicyRequest request) =>
        CanonicalProvenance(new StoredProvenance(
            request.SolutionVersion,
            request.ComponentHash,
            request.OperationId,
            request.StepId,
            request.LeaseEpoch));

    private static string CanonicalProvenance(StoredProvenance provenance) =>
        JsonSerializer.Serialize(new
        {
            solutionVersion = provenance.SolutionVersion,
            componentHash = provenance.ComponentHash,
            operation = provenance.Operation,
            step = provenance.Step,
            leaseEpoch = provenance.LeaseEpoch,
        });

    private static bool TryReadCanonicalProvenance(
        string value,
        out StoredProvenance? provenance)
    {
        try
        {
            provenance = JsonSerializer.Deserialize<StoredProvenance>(
                value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return provenance is not null &&
                IsText(provenance.SolutionVersion, 200) &&
                IsSha256(provenance.ComponentHash) &&
                IsText(provenance.Operation, 120) &&
                IsText(provenance.Step, 120) &&
                provenance.LeaseEpoch >= 0 &&
                StringComparer.Ordinal.Equals(value, CanonicalProvenance(provenance));
        }
        catch (JsonException)
        {
            provenance = null;
            return false;
        }
    }

    private static bool SameReceiptIdentity(
        InstallProductPolicyRequest request,
        StoredProvenance provenance) =>
        StringComparer.Ordinal.Equals(request.SolutionVersion, provenance.SolutionVersion) &&
        StringComparer.Ordinal.Equals(request.ComponentHash, provenance.ComponentHash) &&
        StringComparer.Ordinal.Equals(request.OperationId, provenance.Operation) &&
        StringComparer.Ordinal.Equals(request.StepId, provenance.Step);

    private static Domain.ProductPolicyComponent ToDomain(ProductPolicyComponent value) => new(
        value.PolicyKey,
        value.VersionId,
        value.Roles.Select(role => new Domain.ProductPolicyRole(
            role.RoleKey,
            role.Presentation.ToDictionary(
                value => value.Key,
                value => new Domain.ProductRolePresentation(value.Value.DisplayName, value.Value.Description),
                StringComparer.Ordinal))).ToArray(),
        value.Grants.Select(grant => new Domain.ProductPolicyGrant(
            grant.RoleKey,
            grant.ActionKey,
            grant.ResourceType,
            grant.ResourceKey,
            (Domain.ProductActionScope)grant.Scope)).ToArray());

    private static Domain.ProductActionDescriptor ToDomain(ProductActionDescriptor value) =>
        new(value.ActionKey, value.ResourceType, (Domain.ProductActionKind)value.Kind);
}
