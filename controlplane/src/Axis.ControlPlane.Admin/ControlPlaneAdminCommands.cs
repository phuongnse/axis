using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Axis.ControlPlane.Admin;

/// <summary>
/// The commands of the platform operator for one control plane region, see <c>docs/reference/control-plane.md#operators</c>:
/// <c>sign-ups list</c> and <c>subscriptions list</c>. They read the same <c>ControlPlane:</c> settings as the service and run the same start checks.
/// The exit code is 0 when the command succeeded, 1 when a step failed, such as a database that cannot be read, and 2 when nothing ran
/// because of a usage or settings error. Nothing is written to <c>output</c> in the cases 1 and 2.
/// </summary>
internal static class ControlPlaneAdminCommands
{
    private const string UsageText = """
        Usage:
          sign-ups list
          subscriptions list
        """;

    internal static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);

        Func<ControlPlaneOptions, TextWriter, TextWriter, CancellationToken, Task<int>> command;
        if (args is ["sign-ups", "list"])
        {
            command = RegionLists.SignUpsAsync;
        }
        else if (args is ["subscriptions", "list"])
        {
            command = RegionLists.SubscriptionsAsync;
        }
        else
        {
            error.WriteLine(UsageText);
            return 2;
        }

        // The environment decides whether a database connection must verify the server's certificate, as it does for the service. Unset is Production.
        var environment = new HostingEnvironment
        {
            EnvironmentName = configuration["DOTNET_ENVIRONMENT"] ?? Environments.Production,
            ApplicationName = "Axis.ControlPlane.Admin",
        };

        ControlPlaneOptions options;
        try
        {
            options = ControlPlaneOptions.Load(configuration, environment);
        }
        catch (InvalidOperationException exception)
        {
            // The message names the settings and never their values.
            error.WriteLine(exception.Message);
            return 2;
        }

        return await command(options, output, error, cancellationToken);
    }
}
