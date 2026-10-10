namespace Axis.Server.Users;

/// <summary>A development stand-in for a signed-in user, from the <c>TestUsers</c> configuration section.</summary>
internal sealed class TestUser
{
    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string[] Roles { get; init; } = [];
}
