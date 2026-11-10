using System.Globalization;
using Axis.ControlPlane.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.ControlPlane.Admin;

/// <summary>
/// <c>sign-ups list</c> and <c>subscriptions list</c>: the rows of the region's control plane database with their states and dates,
/// see <c>docs/reference/control-plane.md#operators</c>. They change nothing, so they write no audit record. The tool applies no migration: a database without the schema is exit code 1.
/// </summary>
internal static class RegionLists
{
    private const string Pending = "pending";

    private static readonly string[] _signUpHeader = ["id", "subdomain", "state", "created", "expires"];

    private static readonly string[] _subscriptionHeader = ["id", "tenant", "sign-up", "state", "trial ends", "next", "created"];

    public static async Task<int> SignUpsAsync(ControlPlaneOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<string[]> rows;
        try
        {
            await using var context = Open(options);

            // Only these columns are read, so no name or email address reaches the tool.
            var signUps = await context.SignUps.AsNoTracking()
                .OrderBy(signUp => signUp.CreatedAt).ThenBy(signUp => signUp.Id)
                .Select(signUp => new { signUp.Id, signUp.Subdomain, signUp.State, signUp.CreatedAt, signUp.ExpiresAt })
                .ToListAsync(cancellationToken);
            rows =
            [
                .. signUps.Select(signUp => new[]
                {
                    signUp.Id.ToString(),
                    signUp.Subdomain,
                    signUp.State,
                    Time(signUp.CreatedAt),
                    signUp.State == Pending ? Time(signUp.ExpiresAt) : "-",
                }),
            ];
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(error, "the sign-ups");
        }

        TextTable.Write(output, _signUpHeader, rows);
        return 0;
    }

    public static async Task<int> SubscriptionsAsync(ControlPlaneOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<string[]> rows;
        try
        {
            await using var context = Open(options);

            // Only these columns are read, so nothing personal reaches the tool.
            var subscriptions = await context.Subscriptions.AsNoTracking()
                .OrderBy(subscription => subscription.CreatedAt).ThenBy(subscription => subscription.Id)
                .Select(subscription => new { subscription.Id, subscription.TenantId, subscription.SignUpId, subscription.State, subscription.TrialEndsAt, subscription.NextAt, subscription.CreatedAt })
                .ToListAsync(cancellationToken);
            rows =
            [
                .. subscriptions.Select(subscription => new[]
                {
                    subscription.Id.ToString(),
                    subscription.TenantId,
                    subscription.SignUpId.ToString(),
                    subscription.State,
                    Time(subscription.TrialEndsAt),
                    subscription.NextAt is { } next ? Time(next) : "-",
                    Time(subscription.CreatedAt),
                }),
            ];
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(error, "the subscriptions");
        }

        TextTable.Write(output, _subscriptionHeader, rows);
        return 0;
    }

    private static ControlPlaneDbContext Open(ControlPlaneOptions options) =>
        new(new DbContextOptionsBuilder<ControlPlaneDbContext>().UseNpgsql(options.ConnectionString).Options);

    // Never the exception's text: an Npgsql message can name the host.
    private static int Failed(TextWriter error, string what)
    {
        error.WriteLine($"The setting {ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.ConnectionString)} could not be used to read {what}.");
        return 1;
    }

    internal static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
