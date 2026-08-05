using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Api.Tests.Identity;

public sealed class OpenIddictSeederRegistrationTests
{
    [Fact]
    public void AddIdentityInfrastructure_WhenCatalogIsValid_RegistersAllEnvironmentReconciler()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = "Host=localhost;Database=axis_identity;Username=axis;Password=axis",
                ["OpenIddict:ClientCatalog:SchemaVersion"] = "1",
            })
            .Build();

        services.AddIdentityInfrastructure(configuration);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(OpenIddictSeeder));
    }
}
