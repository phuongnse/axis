using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The TLS settings of a host's own database connection string: <c>SSL Mode</c>, <c>Root Certificate</c> and
/// <c>Check Certificate Revocation</c>. A tenant's connection is built from host, port, user and password, so it copies
/// these settings, and <c>pg_dump</c> and <c>pg_restore</c> take them as <c>PGSSLMODE</c> and <c>PGSSLROOTCERT</c>.
/// Without them a tenant connection would fall back to plain text under the default <c>Prefer</c>.
/// </summary>
public sealed record TenantDatabaseTls(SslMode SslMode, string? RootCertificate, bool CheckCertificateRevocation)
{
    private const string RootCertificateVariable = "PGSSLROOTCERT";

    /// <summary>Reads the TLS settings of <paramref name="connectionString"/>.</summary>
    public static TenantDatabaseTls From(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new TenantDatabaseTls(builder.SslMode, builder.RootCertificate, builder.CheckCertificateRevocation);
    }

    /// <summary>Sets the three TLS settings on <paramref name="builder"/>.</summary>
    public void ApplyTo(NpgsqlConnectionStringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.SslMode = SslMode;
        builder.RootCertificate = RootCertificate;
        builder.CheckCertificateRevocation = CheckCertificateRevocation;
    }

    /// <summary>
    /// Sets <c>PGSSLMODE</c> and <c>PGSSLROOTCERT</c> for a libpq tool. The root certificate is the named file, or
    /// <c>system</c> under <c>VerifyFull</c> when none is named. Otherwise an inherited <c>PGSSLROOTCERT</c> is removed.
    /// libpq has no switch for <c>Check Certificate Revocation</c>.
    /// </summary>
    public void ApplyTo(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        environment["PGSSLMODE"] = LibpqMode(SslMode);
        if (!string.IsNullOrWhiteSpace(RootCertificate))
        {
            environment[RootCertificateVariable] = RootCertificate;
        }
        else if (SslMode == SslMode.VerifyFull)
        {
            environment[RootCertificateVariable] = "system";
        }
        else
        {
            environment.Remove(RootCertificateVariable);
        }
    }

    internal static string LibpqMode(SslMode mode) => mode switch
    {
        SslMode.Disable => "disable",
        SslMode.Allow => "allow",
        SslMode.Prefer => "prefer",
        SslMode.Require => "require",
        SslMode.VerifyCA => "verify-ca",
        SslMode.VerifyFull => "verify-full",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SSL mode."),
    };
}
