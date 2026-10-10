namespace Axis.Server.Users;

/// <summary>
/// The configured test users. They are enabled only in the Development and Testing environments,
/// and startup fails when any are configured in another environment.
/// </summary>
internal sealed class TestUserDirectory
{
    public const string SectionName = "TestUsers";

    private readonly Dictionary<string, TestUser> _users;

    private TestUserDirectory(bool enabled, IEnumerable<TestUser> users)
    {
        Enabled = enabled;
        _users = users.ToDictionary(user => user.Id, StringComparer.Ordinal);
    }

    /// <summary>Whether the environment allows test users, so the sign-in endpoints exist.</summary>
    public bool Enabled { get; }

    public TestUser? Find(string id) => _users.GetValueOrDefault(id);

    /// <summary>Reads the test users from configuration and checks them against the environment.</summary>
    /// <exception cref="InvalidOperationException">
    /// Test users are configured outside the Development and Testing environments, or the list is invalid.
    /// </exception>
    public static TestUserDirectory Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var users = configuration.GetSection(SectionName).Get<List<TestUser>>() ?? [];
        var allowed = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (users.Count > 0 && !allowed)
        {
            throw new InvalidOperationException(
                $"TestUsers is configured in the '{environment.EnvironmentName}' environment. Test users work only in the Development and Testing environments.");
        }

        var problems = Validate(users);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The test user configuration is invalid. {string.Join(" ", problems)}");
        }

        return new TestUserDirectory(allowed, users);
    }

    private static List<string> Validate(List<TestUser> users)
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < users.Count; index++)
        {
            var user = users[index];
            if (string.IsNullOrWhiteSpace(user.Id))
            {
                problems.Add($"Test user {index} has a blank id.");
            }
            else if (!ids.Add(user.Id))
            {
                problems.Add($"Test user id '{user.Id}' is configured more than once.");
            }

            if (string.IsNullOrWhiteSpace(user.DisplayName))
            {
                problems.Add($"Test user {index} has a blank display name.");
            }

            if (user.Roles.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"Test user {index} has a blank role.");
            }
        }

        return problems;
    }
}
