using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Axis.Api.Tests.Identity;

public sealed class OpenIddictSeederRegistrationTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void AddIdentityInfrastructure_WhenEnvironmentChanges_RegistersOpenIddictSeederOnlyForDevelopmentOrTesting(
        string environmentName,
        bool expectedRegistered)
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = "Host=localhost;Database=axis_identity;Username=axis;Password=axis",
            })
            .Build();

        services.AddIdentityInfrastructure(configuration, new TestHostEnvironment(environmentName));

        bool registered = services.Any(
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(OpenIddictSeeder));

        Assert.Equal(expectedRegistered, registered);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Axis.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
