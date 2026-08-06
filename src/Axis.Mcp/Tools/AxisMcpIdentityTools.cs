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
}
