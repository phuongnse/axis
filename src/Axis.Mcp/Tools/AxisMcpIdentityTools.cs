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
}
