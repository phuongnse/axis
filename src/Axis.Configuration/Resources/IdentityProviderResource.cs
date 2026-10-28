namespace Axis.Configuration.Resources;

/// <summary>
/// An <c>identityProvider</c> resource: one way for the application's people to sign in. Its
/// <c>label</c> is held by <see cref="Resource.Label"/>. Its issuer, metadata and client credentials
/// are deployment bindings, named and never held as values. The schema checks the shape of each
/// type. Its type, bindings, roles and uniqueness are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record IdentityProviderResource : Resource
{
    /// <summary><c>accounts</c>, <c>oidc</c> or <c>saml</c>. The schema leaves it a string, the compiler checks it.</summary>
    public required string Type { get; init; }

    /// <summary>An <c>oidc</c> provider's <c>url</c> binding: the address of its discovery document.</summary>
    public BindingReference? Issuer { get; init; }

    /// <summary>An <c>oidc</c> provider's <c>secret</c> binding for the client id.</summary>
    public BindingReference? ClientId { get; init; }

    /// <summary>An <c>oidc</c> provider's <c>secret</c> binding for the client secret, instead of <see cref="ClientKey"/>.</summary>
    public BindingReference? ClientSecret { get; init; }

    /// <summary>An <c>oidc</c> provider's <c>secret</c> binding for a private key, instead of <see cref="ClientSecret"/>.</summary>
    public BindingReference? ClientKey { get; init; }

    /// <summary>A <c>saml</c> provider's <c>url</c> binding: the address of its metadata.</summary>
    public BindingReference? Metadata { get; init; }

    /// <summary>The claims that give the person's details, or null for the defaults.</summary>
    public IdentityProviderClaimsDefinition? Claims { get; init; }

    /// <summary>How the provider's claims give roles, or null for the roles claim.</summary>
    public IdentityProviderRolesDefinition? Roles { get; init; }

    /// <summary>The assurance levels the provider must sign the person in at. Empty when not written.</summary>
    public IReadOnlyList<string> Acr { get; init; } = [];
}

/// <summary>The claims of an identity provider that give the display name, the email and the locale. A claim not written has its default.</summary>
public sealed record IdentityProviderClaimsDefinition
{
    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Locale { get; init; }
}

/// <summary><c>Claim</c> names the claim that gives roles. <c>Values</c> maps a claim value to a role name.</summary>
public sealed record IdentityProviderRolesDefinition
{
    public string? Claim { get; init; }

    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
}
