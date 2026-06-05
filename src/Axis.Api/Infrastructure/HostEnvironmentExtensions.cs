using Microsoft.Extensions.Hosting;

namespace Axis.Api.Infrastructure;

internal static class HostEnvironmentExtensions
{
    public const string TestingEnvironmentName = "Testing";

    public static bool IsTesting(this IHostEnvironment environment) =>
        environment.IsEnvironment(TestingEnvironmentName);

    public static bool IsDevelopmentOrTesting(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsTesting();
}
