namespace Axis.Server.Users;

/// <summary>
/// The user administration settings of the host. <c>Users:AdministratorRole</c> is the roles claim value of an administrator.
/// With no value, nobody is an administrator.
/// </summary>
internal sealed record UsersSettings(string? AdministratorRole)
{
    public const string SectionName = "Users";

    public static UsersSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var role = configuration.GetSection(SectionName)["AdministratorRole"];
        return new UsersSettings(string.IsNullOrWhiteSpace(role) ? null : role);
    }

    /// <summary>True when <see cref="AdministratorRole"/> is set and <paramref name="roles"/> holds it.</summary>
    public bool Allows(IReadOnlyList<string> roles) =>
        AdministratorRole is not null && roles.Contains(AdministratorRole, StringComparer.Ordinal);
}
