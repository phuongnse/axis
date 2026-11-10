using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Axis.ControlPlane.Admin;

/// <summary>
/// The commands of the platform operator for one control plane region, see <c>docs/reference/control-plane.md#operators</c>:
/// <c>sign-ups list</c>, <c>subscriptions list</c>, <c>trial extend</c> and <c>email resend</c>. They read the same <c>ControlPlane:</c> settings as the service and run the same start checks.
/// The exit code is 0 when the command succeeded, 1 when a step failed, such as a database that cannot be read, or when a change was refused, and 2 when nothing ran
/// because of a usage or settings error. Nothing is written to <c>output</c> in the cases 1 and 2.
/// </summary>
internal static class ControlPlaneAdminCommands
{
    private const string UsageText = """
        Usage:
          sign-ups list
          subscriptions list
          trial extend <tenant> --days <n> --operator <name>
          email resend <job id> --operator <name>
        """;

    private const int MaxDays = 365;

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
        else if (args is ["trial", "extend", var tenant, .. var trialOptions]
            && ParseOptions(trialOptions, "--days") is { } trial
            && int.TryParse(trial["--days"], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            && days is >= 1 and <= MaxDays)
        {
            var operatorName = trial["--operator"];
            command = (options, output, error, cancellationToken) => OperatorCommands.TrialExtendAsync(options, tenant, days, operatorName, output, error, cancellationToken);
        }
        else if (args is ["email", "resend", var id, .. var emailOptions]
            && Guid.TryParse(id, out var jobId)
            && ParseOptions(emailOptions) is { } email)
        {
            var operatorName = email["--operator"];
            command = (options, output, error, cancellationToken) => OperatorCommands.EmailResendAsync(options, jobId, operatorName, output, error, cancellationToken);
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

    // Reads the options after the positional words as `--name value` pairs, in any order. It returns null for an option that is unknown, repeated or without a value,
    // and for a missing or blank --operator, or a missing option of `required`. The operator name comes back trimmed.
    private static Dictionary<string, string>? ParseOptions(string[] words, params string[] required)
    {
        var allowed = new HashSet<string>(["--operator", .. required], StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (words.Length % 2 != 0)
        {
            return null;
        }

        for (var i = 0; i < words.Length; i += 2)
        {
            if (!allowed.Contains(words[i]) || !values.TryAdd(words[i], words[i + 1]))
            {
                return null;
            }
        }

        if (!allowed.All(values.ContainsKey) || string.IsNullOrWhiteSpace(values["--operator"]))
        {
            return null;
        }

        values["--operator"] = values["--operator"].Trim();
        return values;
    }
}
