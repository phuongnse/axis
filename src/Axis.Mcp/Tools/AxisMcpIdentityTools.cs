using System.ComponentModel;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpIdentityTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_create_organization_workspace")]
    [Description("[WRITE] Create an Organization and its initial Workspace for the authenticated user. The user and authority are derived from OAuth claims; the idempotency key is forwarded to the API contract.")]
    public Task<string> CreateOrganizationWorkspaceAsync(
        CreateOrganizationWorkspaceInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.IdempotencyKey);
        mutationGuard.EnsureEnabled("CreateOrganizationWorkspace");
        return api.PostIdempotentJsonAsync(
            "api/organizations",
            new CreateOrganizationWorkspaceRequest(input.Name),
            input.IdempotencyKey,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_invite_workspace_member")]
    [Description("[WRITE] Invite a human member to the active Organization Workspace. Organization, Workspace, inviter, and authority are derived from OAuth claims.")]
    public Task<string> InviteWorkspaceMemberAsync(
        InviteWorkspaceMemberInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RequestedRole);
        mutationGuard.EnsureEnabled("InviteWorkspaceMember");
        return api.PostJsonAsync(
            "api/workspace-invitations",
            new InviteWorkspaceMemberRequest(input.Email, input.RequestedRole),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_resend_workspace_invitation")]
    [Description("[WRITE] Supersede every prior link and resend one pending invitation in the active Workspace. The current revision is required; writes are never retried after an ambiguous API result.")]
    public Task<string> ResendWorkspaceInvitationAsync(
        Guid invitationId,
        ChangeWorkspaceInvitationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("ResendWorkspaceInvitation");
        return api.PostJsonAsync(
            $"api/workspace-invitations/{invitationId:D}/resend",
            new ChangeWorkspaceInvitationRequest(input.ExpectedRevision),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_revoke_workspace_invitation")]
    [Description("[WRITE] Idempotently revoke one pending invitation in the active Workspace. The current revision is required; an accepted membership is never removed.")]
    public Task<string> RevokeWorkspaceInvitationAsync(
        Guid invitationId,
        ChangeWorkspaceInvitationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("RevokeWorkspaceInvitation");
        return api.PostJsonAsync(
            $"api/workspace-invitations/{invitationId:D}/revoke",
            new ChangeWorkspaceInvitationRequest(input.ExpectedRevision),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_update_language_preference")]
    [Description("[WRITE] Update the authenticated user's language preference. The user and workspace are derived from OAuth claims.")]
    public Task<string> UpdateLanguagePreferenceAsync(
        [Description("Language code, such as en.")] string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        mutationGuard.EnsureEnabled("UpdateLanguagePreference");
        return api.PutJsonAsync(
            "api/users/me/preferences/language",
            new LanguagePreferenceRequest(language),
            cancellationToken);
    }

    [McpServerTool(Name = "axis_update_theme_preference")]
    [Description("[WRITE] Update the authenticated user's theme preference. The user and workspace are derived from OAuth claims.")]
    public Task<string> UpdateThemePreferenceAsync(
        [Description("Theme name accepted by the Axis preference contract.")] string theme,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        mutationGuard.EnsureEnabled("UpdateThemePreference");
        return api.PutJsonAsync(
            "api/users/me/preferences/theme",
            new ThemePreferenceRequest(theme),
            cancellationToken);
    }

    private sealed record LanguagePreferenceRequest(string Language);
    private sealed record ThemePreferenceRequest(string Theme);
    private sealed record CreateOrganizationWorkspaceRequest(string Name);
    private sealed record InviteWorkspaceMemberRequest(string Email, string RequestedRole);
    private sealed record ChangeWorkspaceInvitationRequest(int ExpectedRevision);
}

[McpServerToolType]
public sealed class AxisMcpIdentityReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_workspace_invitations")]
    [Description("[READ] List non-secret invitation lifecycle outcomes for the active Organization Workspace. Authority is derived from OAuth claims.")]
    public Task<string> ListWorkspaceInvitationsAsync(
        [Description("One-based page number.")] int page = 1,
        [Description("Items per page, maximum 100.")] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        api.GetJsonAsync(
            $"api/workspace-invitations?page={page}&pageSize={pageSize}",
            cancellationToken);
}
