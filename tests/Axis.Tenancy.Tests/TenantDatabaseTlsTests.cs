using Npgsql;

namespace Axis.Tenancy.Tests;

public sealed class TenantDatabaseTlsTests
{
    [Theory]
    [InlineData(SslMode.Disable, "disable")]
    [InlineData(SslMode.Allow, "allow")]
    [InlineData(SslMode.Prefer, "prefer")]
    [InlineData(SslMode.Require, "require")]
    [InlineData(SslMode.VerifyCA, "verify-ca")]
    [InlineData(SslMode.VerifyFull, "verify-full")]
    public void Each_ssl_mode_maps_to_its_libpq_value(SslMode mode, string expected)
    {
        var environment = new Dictionary<string, string?>();

        new TenantDatabaseTls(mode, null, false).ApplyTo(environment);

        Assert.Equal(expected, environment["PGSSLMODE"]);
        Assert.Equal(expected, TenantDatabaseTls.LibpqMode(mode));
    }

    [Fact]
    public void A_named_root_certificate_is_the_root_certificate_of_the_tools()
    {
        var environment = new Dictionary<string, string?>();

        TenantDatabaseTls.From("Host=x;SSL Mode=VerifyCA;Root Certificate=/certs/root.pem").ApplyTo(environment);

        Assert.Equal("/certs/root.pem", environment["PGSSLROOTCERT"]);
    }

    [Fact]
    public void Verify_full_without_a_root_certificate_uses_the_system_store()
    {
        var environment = new Dictionary<string, string?>();

        TenantDatabaseTls.From("Host=x;SSL Mode=VerifyFull").ApplyTo(environment);

        Assert.Equal("system", environment["PGSSLROOTCERT"]);
    }

    [Theory]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    public void A_mode_weaker_than_verify_full_without_a_root_certificate_removes_an_inherited_one(string mode)
    {
        var environment = new Dictionary<string, string?> { ["PGSSLROOTCERT"] = "/inherited.pem" };

        TenantDatabaseTls.From($"Host=x;SSL Mode={mode}").ApplyTo(environment);

        Assert.False(environment.ContainsKey("PGSSLROOTCERT"));
    }

    [Fact]
    public void No_ssl_mode_is_prefer()
    {
        var tls = TenantDatabaseTls.From("Host=x");
        var environment = new Dictionary<string, string?>();

        tls.ApplyTo(environment);

        Assert.Equal(SslMode.Prefer, tls.SslMode);
        Assert.Equal("prefer", environment["PGSSLMODE"]);
    }

    [Fact]
    public void The_three_settings_are_copied_onto_a_connection_string_builder()
    {
        var tls = TenantDatabaseTls.From("Host=x;SSL Mode=VerifyFull;Root Certificate=/certs/root.pem;Check Certificate Revocation=true");
        var builder = new NpgsqlConnectionStringBuilder { Host = "tenant-host" };

        tls.ApplyTo(builder);

        var copied = new NpgsqlConnectionStringBuilder(builder.ConnectionString);
        Assert.Equal(SslMode.VerifyFull, copied.SslMode);
        Assert.Equal("/certs/root.pem", copied.RootCertificate);
        Assert.True(copied.CheckCertificateRevocation);
        Assert.Equal("tenant-host", copied.Host);
    }
}
