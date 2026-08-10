using System.Security.Cryptography;
using System.Text;
using Axis.Audit.Contracts;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;

namespace Axis.Authorization.Application;

public interface IProductPolicyReadStore
{
    Task<IReadOnlyList<ProductPolicyGrant>> ListActiveGrantsAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default);
}

public interface IProductActionDescriptorRegistry
{
    ProductActionDescriptor? Find(string actionKey, string resourceType);
}

public sealed class ProductAuthorizationService(
    IAuthorizationSubjectActivity activity,
    IProductActionDescriptorRegistry descriptors,
    IProductPolicyReadStore store,
    IAuthorizationAuditSink audit,
    IAuthorizationUnitOfWork unitOfWork,
    TimeProvider clock) : IProductAuthorizationService
{
    public async Task<ProductAuthorizationDecision> AuthorizeAsync(
        ProductAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkspaceId == Guid.Empty
            || request.Subject.Id == Guid.Empty
            || !Enum.IsDefined(request.Subject.Kind)
            || string.IsNullOrWhiteSpace(request.ActionKey)
            || request.ActionKey.Length > 200
            || string.IsNullOrWhiteSpace(request.ResourceType)
            || request.ResourceType.Length > 200
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Trim().Length > AuditEventV1Validator.MaximumCorrelationIdLength)
        {
            return await PersistInvalidRequestAsync(request, cancellationToken)
                ? ProductAuthorizationDecision.Denied
                : ProductAuthorizationDecision.Unavailable;
        }

        ProductAuthorizationDecision decision = ProductAuthorizationDecision.Denied;
        string outcome = "denied";
        try
        {
            if (await activity.IsActiveAsync(
                    request.WorkspaceId,
                    request.Subject,
                    cancellationToken))
            {
                ProductActionDescriptor? descriptor = descriptors.Find(
                    request.ActionKey,
                    request.ResourceType);
                if (descriptor is not null)
                {
                    IReadOnlyList<ProductPolicyGrant> grants =
                        await store.ListActiveGrantsAsync(
                            request.WorkspaceId,
                            request.Subject,
                            cancellationToken);
                    ProductActionScope? scope = grants
                        .Where(grant =>
                            StringComparer.Ordinal.Equals(
                                grant.ActionKey,
                                request.ActionKey)
                            && StringComparer.Ordinal.Equals(
                                grant.ResourceType,
                                request.ResourceType)
                            && StringComparer.Ordinal.Equals(
                                grant.ResourceKey,
                                request.ResourceKey)
                            && IsCompatible(descriptor.Kind, grant.Scope))
                        .Select(grant => (ProductActionScope?)grant.Scope)
                        .OrderByDescending(value => value)
                        .FirstOrDefault();
                    if (scope is not null)
                    {
                        decision = new ProductAuthorizationDecision(true, scope);
                        outcome = "allowed";
                    }
                }
            }
        }
        catch
        {
            decision = ProductAuthorizationDecision.Unavailable;
            outcome = "dependency_failure";
        }

        return await PersistDecisionAsync(request, decision, outcome, cancellationToken)
            ? decision
            : ProductAuthorizationDecision.Unavailable;
    }

    private async Task<bool> PersistDecisionAsync(
        ProductAuthorizationRequest request,
        ProductAuthorizationDecision decision,
        string outcome,
        CancellationToken cancellationToken)
    {
        AuditEventV1 auditEvent = new(
            Guid.NewGuid(),
            request.Subject.Kind == SubjectKind.Service
                ? AuditActorKindV1.ServiceIdentity
                : AuditActorKindV1.Human,
            request.Subject.Id,
            request.Subject.Id,
            request.WorkspaceId,
            "authorization.policy_decision",
            "product-action",
            TargetId(request),
            outcome,
            clock.GetUtcNow(),
            request.CorrelationId.Trim(),
            new Dictionary<string, string>
            {
                ["action"] = request.ActionKey,
                ["resource"] = request.ResourceType,
                ["scope"] = decision.IsAllowed
                    ? decision.Scope!.Value.ToString()
                    : decision.Status.ToString(),
            });

        return await PersistAuditAsync(auditEvent, cancellationToken);
    }

    private async Task<bool> PersistInvalidRequestAsync(
        ProductAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        Guid eventId = Guid.NewGuid();
        bool hasActor = request.WorkspaceId != Guid.Empty
            && request.Subject.Id != Guid.Empty
            && Enum.IsDefined(request.Subject.Kind);
        AuditActorKindV1 actorKind = hasActor
            ? request.Subject.Kind == SubjectKind.Service
                ? AuditActorKindV1.ServiceIdentity
                : AuditActorKindV1.Human
            : AuditActorKindV1.Anonymous;
        string correlationId = !string.IsNullOrWhiteSpace(request.CorrelationId)
            && request.CorrelationId.Trim().Length <= AuditEventV1Validator.MaximumCorrelationIdLength
                ? request.CorrelationId.Trim()
                : $"authorization-{eventId:N}";
        AuditEventV1 auditEvent = new(
            eventId,
            actorKind,
            hasActor ? request.Subject.Id : null,
            hasActor ? request.Subject.Id : null,
            request.WorkspaceId == Guid.Empty ? null : request.WorkspaceId,
            "authorization.policy_decision",
            "product-action",
            InvalidTargetId(request.WorkspaceId),
            "invalid_request",
            clock.GetUtcNow(),
            correlationId,
            new Dictionary<string, string> { ["request"] = "invalid" });
        return await PersistAuditAsync(auditEvent, cancellationToken);
    }

    private async Task<bool> PersistAuditAsync(
        AuditEventV1 auditEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.BeginAsync(cancellationToken);
            AuditIngestionResult ingestion = await audit.IngestAsync(
                auditEvent,
                cancellationToken);
            if (ingestion.Disposition is AuditIngestionDisposition.Conflict
                or AuditIngestionDisposition.Rejected)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return false;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            AuditEventReadBackV1? readBack = await audit.ReadBackAsync(
                auditEvent.EventId,
                cancellationToken);
            if (readBack is null || !AuditEventV1ReadBack.Matches(auditEvent, readBack))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return false;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            try
            {
                await unitOfWork.RollbackAsync(cancellationToken);
            }
            catch
            {
                // The authorization decision remains unavailable even if rollback also fails.
            }
            return false;
        }
    }

    private static bool IsCompatible(
        ProductActionKind kind,
        ProductActionScope scope) =>
        kind switch
        {
            ProductActionKind.NonRecord => scope == ProductActionScope.None,
            ProductActionKind.Record => scope is ProductActionScope.Own or ProductActionScope.All,
            _ => false,
        };

    private static Guid TargetId(ProductAuthorizationRequest request)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{request.WorkspaceId:N}\u001f{request.ActionKey}\u001f{request.ResourceType}\u001f{request.ResourceKey}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static Guid InvalidTargetId(Guid workspaceId)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                workspaceId == Guid.Empty
                    ? "authorization.policy_decision.invalid"
                    : $"authorization.policy_decision.invalid:{workspaceId:N}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
