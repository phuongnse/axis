using System.ComponentModel;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpAuthorizationReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_product_role_assignments")]
    [Description("[READ] List active assignable Human and Service subjects, installed exact product roles, and current assignments in the authenticated Workspace.")]
    public Task<string> ListProductRoleAssignmentsAsync(
        [Description("Optional BCP 47 response language, such as en.")] string? language = null,
        CancellationToken cancellationToken = default) =>
        api.GetJsonAsync(
            "api/product-role-assignments" + AxisApiQuery.Build(("language", language)),
            cancellationToken);
}

[McpServerToolType]
public sealed class AxisMcpAuthorizationTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_assign_product_role")]
    [Description("[WRITE] Assign one exact installed product role to an active Human or Service subject in the authenticated current Workspace.")]
    public Task<string> AssignProductRoleAsync(
        AssignProductRoleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input.Target, input.PolicyVersionId, input.RoleKey, input.IdempotencyKey);
        mutationGuard.EnsureEnabled("AssignProductRole");
        return api.PostIdempotentJsonAsync(
            "api/product-role-assignments/assign",
            new AssignProductRoleRequest(
                input.Target,
                input.PolicyVersionId,
                input.RoleKey,
                input.ExpectedRevision),
            input.IdempotencyKey,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_revoke_product_role")]
    [Description("[WRITE/DESTRUCTIVE] Revoke one exact product-role assignment for a Human or Service subject in the authenticated current Workspace.")]
    public Task<string> RevokeProductRoleAsync(
        RevokeProductRoleInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input.Target, input.PolicyVersionId, input.RoleKey, input.IdempotencyKey);
        mutationGuard.EnsureEnabled("RevokeProductRole");
        return api.PostIdempotentJsonAsync(
            "api/product-role-assignments/revoke",
            new RevokeProductRoleRequest(
                input.Target,
                input.PolicyVersionId,
                input.RoleKey,
                input.ExpectedRevision),
            input.IdempotencyKey,
            cancellationToken);
    }

    private static void Validate(
        SubjectReferenceInput target,
        Guid policyVersionId,
        string roleKey,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind is not ("Human" or "Service"))
            throw new ArgumentException("target.kind must be Human or Service.", nameof(target));
        if (target.SubjectId == Guid.Empty)
            throw new ArgumentException("target.subjectId must be a non-empty UUID.", nameof(target));
        if (policyVersionId == Guid.Empty)
            throw new ArgumentException("policyVersionId must be a non-empty UUID.", nameof(policyVersionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(roleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
    }

    private sealed record AssignProductRoleRequest(
        SubjectReferenceInput Target,
        Guid PolicyVersionId,
        string RoleKey,
        int? ExpectedRevision);

    private sealed record RevokeProductRoleRequest(
        SubjectReferenceInput Target,
        Guid PolicyVersionId,
        string RoleKey,
        int ExpectedRevision);
}
