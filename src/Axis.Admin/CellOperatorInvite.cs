using Axis.Administration;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Axis.Admin;

/// <summary>
/// <c>cell operator invite &lt;email&gt;</c>. In a cell it makes the operator realm, or brings it up to date, and invites the first
/// platform operator: it finds or creates the account, gives it the operator role and has the realm email the link. It writes one
/// platform audit record with no tenant, before it changes anything. The record and the output name the realm and never the email.
/// Outside a cell it refuses and changes nothing.
/// </summary>
internal static class CellOperatorInvite
{
    public static async Task<int> InviteAsync(AdminRun run)
    {
        var email = run.Args.Length == 4 ? RealmUsers.NormalizeEmail(run.Args[3]) : null;
        if (email is null)
        {
            run.Error.WriteLine(AdminCommands.Usage);
            return 2;
        }

        if (!run.Cell.IsCell)
        {
            run.Error.WriteLine("This command runs only in a cell: Cell:Id is not set.");
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

        if (AdminCommands.ReadRealmMail(configuration, out problem) is not { } mail)
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
        if (!await WriteAuditAsync(run, platformConnection, realm, role))
        {
            return 1;
        }

        using var http = AdminCommands.IdentityProviderClient(run.IdentityProviderHandler);
        var operators = new OperatorRealm(http, identityProvider);

        if (!await StepAsync(run, realm, "realm", () => operators.EnsureRealmAsync(realm, role, mail.For(null), run.CancellationToken)))
        {
            return 1;
        }

        var userId = "";
        var created = false;
        if (!await StepAsync(run, realm, "account", async () => (userId, created) = await operators.EnsureOperatorAsync(realm, role, email, run.CancellationToken)))
        {
            return 1;
        }

        // A person who has set a password needs no new link.
        var skipped = false;
        var invited = await StepAsync(
            run,
            realm,
            "invitation",
            async () =>
            {
                skipped = !created && await operators.HasPasswordAsync(realm, userId, run.CancellationToken);
                if (!skipped)
                {
                    await operators.SendInvitationAsync(realm, userId, run.CancellationToken);
                }
            },
            () => skipped ? "skipped" : "ok");
        return invited ? 0 : 1;
    }

    private static async Task<bool> WriteAuditAsync(AdminRun run, string platformConnection, string realm, string role)
    {
        try
        {
            run.CancellationToken.ThrowIfCancellationRequested();
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnection).Options);
            await PlatformAuditRecords.WriteAsync(platform, run.Command.Name, null, [$"realm: {realm}", $"role: {role}"], run.CancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // A save failure wraps the database error, whose message is the one that helps the operator.
            var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
            run.Output.WriteLine($"{realm} audit failed: {TenantDatabaseSteps.Safe(cause)}");
            return false;
        }
    }

    // Runs one step. Any failure, also a stop by Ctrl+C, writes a failed line.
    private static async Task<bool> StepAsync(AdminRun run, string realm, string step, Func<Task> work, Func<string>? result = null)
    {
        try
        {
            run.CancellationToken.ThrowIfCancellationRequested();
            await work();
        }
        catch (Exception exception)
        {
            run.Output.WriteLine($"{realm} {step} failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }

        run.Output.WriteLine($"{realm} {step} {result?.Invoke() ?? "ok"}");
        return true;
    }
}
