namespace Axis.Server.Users;

/// <summary>
/// The operator console settings of the host. <c>Console:OperatorRole</c> is the roles claim value of an operator. With no
/// value, nobody is an operator.
/// </summary>
internal sealed record ConsoleSettings(string? OperatorRole)
{
    public const string SectionName = "Console";

    public static ConsoleSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var role = configuration.GetSection(SectionName)["OperatorRole"];
        return new ConsoleSettings(string.IsNullOrWhiteSpace(role) ? null : role);
    }

    /// <summary>True when <see cref="OperatorRole"/> is set and <paramref name="roles"/> holds it.</summary>
    public bool Allows(IReadOnlyList<string> roles) =>
        OperatorRole is not null && roles.Contains(OperatorRole, StringComparer.Ordinal);
}
