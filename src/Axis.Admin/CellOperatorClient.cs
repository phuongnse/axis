using System.Text.RegularExpressions;
using Axis.Tenancy.Realms;
using Microsoft.Extensions.Hosting;

namespace Axis.Admin;

/// <summary>
/// <c>cell operator client add &lt;name&gt;</c>. In a cell it makes a confidential client of the operator realm that signs in with the
/// client credentials grant and whose service account holds the operator role, or brings the client of that name up to date. It prints the
/// secret once, when it makes the client. It writes one platform audit record with no tenant, before it changes anything. The record
/// names the realm, the role and the client and never the secret. The realm and the role must exist, which <c>cell operator invite</c>
/// makes. Outside a cell it refuses and changes nothing.
/// </summary>
internal static partial class CellOperatorClient
{
    private const int NameLength = 64;

    private static readonly string[] _builtInClients =
        ["account", "account-console", "admin-cli", "broker", "realm-management", "security-admin-console"];

    public static async Task<int> AddAsync(AdminRun run)
    {
        var name = run.Args.Length == 5 && ValidName(run.Args[4]) ? run.Args[4] : null;
        if (name is null)
        {
            run.Error.WriteLine(AdminCommands.Usage);
            return 2;
        }

        if (!run.Cell.IsCell)
        {
            run.Error.WriteLine("This command runs only in a cell: Cell:Id is not set.");
            return 2;
        }

        if (name == run.Cell.ConsoleClientId || _builtInClients.Contains(name, StringComparer.Ordinal))
        {
            run.Error.WriteLine($"The name '{name}' belongs to a client that signs people in. Choose another name.");
            return 2;
        }

        var configuration = run.Configuration;
        var platformConnection = AdminCommands.ReadDirectorySettings(configuration, out var problem);
        if (platformConnection is null)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        var environment = new AdminEnvironment(configuration[AdminCommands.DotnetEnvironmentSetting] ?? Environments.Production);
        if (AdminCommands.ReadIdentityProvider(configuration, environment, out problem) is not { } identityProvider)
        {
            run.Error.WriteLine(problem);
            return 2;
        }

        try
        {
            var tablesProblem = await AdminCommands.PlatformTablesProblemAsync(platformConnection, run.CancellationToken);
            if (tablesProblem is not null)
            {
                run.Error.WriteLine(tablesProblem);
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            run.Error.WriteLine("The command was canceled before any change.");
            return 1;
        }

        var realm = run.Cell.OperatorRealm;
        var role = run.Cell.OperatorRole;
        if (!await CellOperatorInvite.WriteAuditAsync(run, platformConnection, realm, [$"realm: {realm}", $"role: {role}", $"client: {name}"]))
        {
            return 1;
        }

        using var http = AdminCommands.IdentityProviderClient(run.IdentityProviderHandler);
        var operators = new OperatorRealm(http, identityProvider);

        string? secret = null;
        if (!await CellOperatorInvite.StepAsync(run, realm, "client", async () => secret = await operators.EnsureMachineClientAsync(realm, role, name, run.CancellationToken)))
        {
            return 1;
        }

        if (secret is not null)
        {
            run.Output.WriteLine($"{realm} client-secret {secret}");
        }

        return 0;
    }

    // Lowercase letters and digits joined by single hyphens, like a cell id.
    private static bool ValidName(string name) => name.Length <= NameLength && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
