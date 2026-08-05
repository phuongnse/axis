using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Services;

public enum OpenIddictClientProfile
{
    NativePublic,
    WebBffConfidential,
}

public sealed record OpenIddictClientCatalog(
    int SchemaVersion,
    IReadOnlyList<OpenIddictClientRegistration> Clients)
{
    public const int CurrentSchemaVersion = 1;
    public const string SectionName = "OpenIddict:ClientCatalog";

    public static OpenIddictClientCatalog Load(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        if (!section.Exists())
            throw new InvalidOperationException($"Required configuration section '{SectionName}' is missing.");

        ClientCatalogConfiguration configured;
        try
        {
            configured = section.Get<ClientCatalogConfiguration>(options =>
                options.ErrorOnUnknownConfiguration = true)
                ?? throw new InvalidOperationException("The client catalog is empty.");
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"{SectionName} contains unknown or invalid configuration.",
                exception);
        }

        if (configured.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"{SectionName}:SchemaVersion must be {CurrentSchemaVersion}.");

        List<OpenIddictClientRegistration> clients = configured.Clients
            .Select((client, index) => ValidateClient(client, index))
            .OrderBy(client => client.ClientId, StringComparer.Ordinal)
            .ToList();

        string? duplicateClientId = clients
            .GroupBy(client => client.ClientId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateClientId is not null)
            throw new InvalidOperationException($"{SectionName} contains duplicate client ID '{duplicateClientId}'.");

        return new OpenIddictClientCatalog(configured.SchemaVersion, clients);
    }

    private static OpenIddictClientRegistration ValidateClient(
        ClientRegistrationConfiguration configured,
        int index)
    {
        string path = $"{SectionName}:Clients:{index}";
        string clientId = RequiredCanonical(configured.ClientId, $"{path}:ClientId", 100);
        if (!IsSemanticClientId(clientId))
        {
            throw new InvalidOperationException(
                $"{path}:ClientId must start with a lowercase letter and contain only lowercase letters, digits, '.', '_' or '-'.");
        }

        string displayName = RequiredCanonical(configured.DisplayName, $"{path}:DisplayName", 200);
        OpenIddictClientProfile profile = ParseProfile(configured.Profile, $"{path}:Profile");
        if (configured.RedirectUris.Count == 0)
            throw new InvalidOperationException($"{path}:RedirectUris must contain at least one URI.");

        Uri[] redirectUris = configured.RedirectUris
            .Select((value, uriIndex) => ParseRedirectUri(value, $"{path}:RedirectUris:{uriIndex}"))
            .ToArray();
        Uri[] postLogoutRedirectUris = configured.PostLogoutRedirectUris
            .Select((value, uriIndex) => ParseRedirectUri(value, $"{path}:PostLogoutRedirectUris:{uriIndex}"))
            .ToArray();
        EnsureDistinct(redirectUris, $"{path}:RedirectUris");
        EnsureDistinct(postLogoutRedirectUris, $"{path}:PostLogoutRedirectUris");

        string? clientSecret = configured.ClientSecret;
        if (profile == OpenIddictClientProfile.NativePublic)
        {
            if (clientSecret is not null)
                throw new InvalidOperationException($"{path}:ClientSecret is not allowed for NativePublic clients.");
            if (postLogoutRedirectUris.Length != 0)
                throw new InvalidOperationException($"{path}:PostLogoutRedirectUris is not allowed for NativePublic clients.");
            if (redirectUris.Any(uri => !uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException($"{path} NativePublic redirects must use exact HTTP loopback URIs.");
        }
        else
        {
            clientSecret = RequiredCanonical(clientSecret, $"{path}:ClientSecret", 512);
            if (clientSecret.Length < 32)
                throw new InvalidOperationException($"{path}:ClientSecret must contain at least 32 characters.");
            if (postLogoutRedirectUris.Length == 0)
                throw new InvalidOperationException($"{path}:PostLogoutRedirectUris must contain at least one URI.");
            if (redirectUris.Concat(postLogoutRedirectUris).Any(uri => uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException($"{path} WebBffConfidential redirects must use HTTPS.");
        }

        return new OpenIddictClientRegistration(
            clientId,
            displayName,
            profile,
            clientSecret,
            redirectUris,
            postLogoutRedirectUris);
    }

    private static OpenIddictClientProfile ParseProfile(string? value, string path)
    {
        string canonical = RequiredCanonical(value, path, 50);
        if (Enum.TryParse(canonical, ignoreCase: false, out OpenIddictClientProfile profile) &&
            Enum.IsDefined(profile))
        {
            return profile;
        }

        throw new InvalidOperationException(
            $"{path} must be NativePublic or WebBffConfidential.");
    }

    private static Uri ParseRedirectUri(string? value, string path)
    {
        string canonical = RequiredCanonical(value, path, 2_048);
        if (!Uri.TryCreate(canonical, UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.Host.Contains('*', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{path} must be an exact absolute redirect URI without wildcard, user info, or fragment.");
        }

        return uri;
    }

    private static string RequiredCanonical(string? value, string path, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{path} is required.");
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"{path} must not contain surrounding whitespace.");
        if (value.Length > maxLength)
            throw new InvalidOperationException($"{path} cannot exceed {maxLength} characters.");
        return value;
    }

    private static void EnsureDistinct(IEnumerable<Uri> values, string path)
    {
        string[] materialized = values.Select(value => value.AbsoluteUri).ToArray();
        if (materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new InvalidOperationException($"{path} cannot contain duplicate values.");
    }

    private static bool IsSemanticClientId(string value) =>
        value.Length > 0 &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');

    private sealed class ClientCatalogConfiguration
    {
        public int SchemaVersion { get; init; }
        public List<ClientRegistrationConfiguration> Clients { get; init; } = [];
    }

    private sealed class ClientRegistrationConfiguration
    {
        public string? ClientId { get; init; }
        public string? DisplayName { get; init; }
        public string? Profile { get; init; }
        public string? ClientSecret { get; init; }
        public List<string?> RedirectUris { get; init; } = [];
        public List<string?> PostLogoutRedirectUris { get; init; } = [];
    }
}

public sealed record OpenIddictClientRegistration(
    string ClientId,
    string DisplayName,
    OpenIddictClientProfile Profile,
    string? ClientSecret,
    IReadOnlyList<Uri> RedirectUris,
    IReadOnlyList<Uri> PostLogoutRedirectUris);
