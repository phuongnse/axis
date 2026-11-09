using System.Collections.Frozen;
using Axis.Core.Tenancy;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>The answer of the subdomain check. <see cref="Reason"/> is null when the name is free. It never names a tenant, a cell or a host.</summary>
internal sealed record SubdomainAnswer(bool Free, string? Reason);

/// <summary>
/// Tells whether a subdomain is free for sign-up, see <c>docs/reference/sign-up.md#the-subdomain</c>. The checks run in a fixed
/// order and the first that fails gives the reason: <c>invalid</c>, <c>reserved</c>, <c>taken</c>, then <c>held</c>. A name is
/// held by a deletion in the directory, or by a pending sign-up of the control plane that has not expired. A malformed
/// name never reaches the database. The answer is advice: the tenant row's primary key still refuses a duplicate.
/// </summary>
internal sealed class SubdomainCheck(NpgsqlDataSource directory, NpgsqlDataSource controlPlane, ControlPlaneOptions options)
{
    public const string Invalid = "invalid";
    public const string Reserved = "reserved";
    public const string Taken = "taken";
    public const string Held = "held";

    private const int MaxLength = 40;

    // The region's own host names, as in docs/reference/region-directory.md#routing-a-host.
    private static readonly FrozenSet<string> _reservedNames = new[]
    {
        "account", "admin", "api", "app", "auth", "billing", "console", "docs", "help", "id", "ingress", "login", "mail",
        "signup", "sign-up", "static", "status", "support", "www",
    }.ToFrozenSet(StringComparer.Ordinal);

    public async Task<SubdomainAnswer> CheckAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!TenantIds.IsValid(name) || name.Length > MaxLength || name == "master")
        {
            return Not(Invalid);
        }

        if (_reservedNames.Contains(name) || options.Cells.Any(cell => string.Equals(cell.Id, name, StringComparison.Ordinal)))
        {
            return Not(Reserved);
        }

        // A cell of any region is reserved, and a null answer is free. A hold counts while it has no end or the end is ahead.
        await using var command = directory.CreateCommand("""
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM directory.cells WHERE id = @name) THEN 'reserved'
                WHEN EXISTS (SELECT 1 FROM directory.tenants WHERE id = @name)
                    OR EXISTS (SELECT 1 FROM directory.hosts WHERE host = @host) THEN 'taken'
                WHEN EXISTS (
                    SELECT 1 FROM directory.held
                    WHERE ((kind = 'tenant' AND value = @name) OR (kind = 'host' AND value = @host))
                        AND (held_until IS NULL OR held_until > now())) THEN 'held'
            END
            """);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("host", $"{name}.{options.TenantDomain!.ToLowerInvariant()}");
        var reason = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (reason is not null)
        {
            return Not(reason);
        }

        // The pending sign-ups are in the control plane's database, which the directory's connection cannot read.
        await using var pending = controlPlane.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM control_plane.sign_ups WHERE subdomain = @name AND state = 'pending' AND expires_at > now())");
        pending.Parameters.AddWithValue("name", name);
        return (bool)(await pending.ExecuteScalarAsync(cancellationToken))! ? Not(Held) : new SubdomainAnswer(true, null);
    }

    private static SubdomainAnswer Not(string reason) => new(false, reason);
}
