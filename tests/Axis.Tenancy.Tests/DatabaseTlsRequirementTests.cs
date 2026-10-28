using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy.Tests;

/// <summary>Outside Development and Testing, a host's own connection string must set <c>SSL Mode=VerifyFull</c>.</summary>
public sealed class DatabaseTlsRequirementTests
{
    private const string Setting = "ConnectionStrings:Platform";
    private const string Password = "db-p4ssw0rd";

    [Theory]
    [InlineData("Disable")]
    [InlineData("Allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    public void A_mode_below_verify_full_is_refused_naming_the_setting_and_not_the_password(string mode)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTlsRequirement.Require(new StubEnvironment("Production"), Setting, $"Host=db.example.test;Password={Password};SSL Mode={mode}"));

        Assert.Contains(Setting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("VerifyFull", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_ssl_mode_is_refused_because_it_means_prefer()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTlsRequirement.Require(new StubEnvironment("Production"), Setting, $"Host=db.example.test;Password={Password}"));

        Assert.Contains(Setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_full_is_accepted()
    {
        DatabaseTlsRequirement.Require(new StubEnvironment("Production"), Setting, $"Host=db.example.test;Password={Password};SSL Mode=VerifyFull");

        Assert.Null(DatabaseTlsRequirement.Problem(Setting, "Host=db.example.test;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_value_is_left_to_the_check_that_the_setting_is_present(string? value)
    {
        Assert.Null(DatabaseTlsRequirement.Problem(Setting, value));
    }

    [Fact]
    public void A_value_that_is_not_a_connection_string_is_refused_without_showing_it()
    {
        var problem = DatabaseTlsRequirement.Problem(Setting, $"Host=db.example.test;Password={Password};SSL Mode=Maybe");

        Assert.NotNull(problem);
        Assert.Contains(Setting, problem, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public void Only_development_and_testing_are_exempt(string environment, bool applies)
    {
        var host = new StubEnvironment(environment);

        Assert.Equal(applies, DatabaseTlsRequirement.Applies(host));
        if (!applies)
        {
            DatabaseTlsRequirement.Require(host, Setting, "Host=db.example.test;SSL Mode=Disable");
        }
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Axis.Tenancy.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
