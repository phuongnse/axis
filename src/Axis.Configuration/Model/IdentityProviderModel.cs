namespace Axis.Configuration.Model;

/// <summary>
/// A compiled identity provider: one way for the application's people to sign in. Its issuer,
/// metadata and client credentials are deployment bindings, held only by name.
/// </summary>
public sealed record IdentityProviderModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The provider's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary><c>accounts</c>, <c>oidc</c> or <c>saml</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The text key of the "Continue with" button, or null for <c>accounts</c>.</summary>
    public string? LabelTextKey { get; init; }

    /// <summary>The name of an <c>oidc</c> provider's <c>url</c> binding for its discovery document.</summary>
    public string? IssuerBinding { get; init; }

    /// <summary>The name of an <c>oidc</c> provider's <c>secret</c> binding for the client id.</summary>
    public string? ClientIdBinding { get; init; }

    /// <summary>The name of an <c>oidc</c> provider's <c>secret</c> binding for the client secret, or null when it has a key.</summary>
    public string? ClientSecretBinding { get; init; }

    /// <summary>The name of an <c>oidc</c> provider's <c>secret</c> binding for a private key, or null when it has a secret.</summary>
    public string? ClientKeyBinding { get; init; }

    /// <summary>The name of a <c>saml</c> provider's <c>url</c> binding for its metadata.</summary>
    public string? MetadataBinding { get; init; }

    /// <summary>The claims with their defaults filled in, or null for <c>accounts</c>.</summary>
    public IdentityProviderClaimsModel? Claims { get; init; }

    /// <summary>How the provider's claims give roles.</summary>
    public required IdentityProviderRolesModel Roles { get; init; }

    /// <summary>The assurance levels the provider must sign the person in at. Empty when not written.</summary>
    public required IReadOnlyList<string> Acr { get; init; }
}

/// <summary>The claims that give the display name, the email and the locale: <c>name</c>, <c>email</c> and <c>locale</c> unless written.</summary>
public sealed record IdentityProviderClaimsModel(string DisplayName, string Email, string Locale);

/// <summary>
/// <see cref="Claim"/> names the claim that gives roles: <c>roles</c> unless written. <see cref="Values"/>
/// maps a claim value to the name of a declared role, and is empty when a claim value gives the role of the same name.
/// </summary>
public sealed record IdentityProviderRolesModel(string Claim, IReadOnlyDictionary<string, string> Values);
