using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Processes.Instances;

/// <summary>
/// The mail settings of the host, read from its <c>Email</c> section. A tenant with an <c>Email</c> block of its own
/// uses that block whole instead.
/// </summary>
/// <param name="Settings">The host's settings, or <see langword="null"/> when it has no <c>Email</c> section.</param>
public sealed record EmailDefaults(EmailSettings? Settings)
{
    /// <summary>The name of the configuration section.</summary>
    public const string SectionName = "Email";

    /// <summary>Reads the <c>Email</c> section of <paramref name="configuration"/>.</summary>
    /// <exception cref="InvalidOperationException">The section is invalid. The message names each setting and shows no value.</exception>
    public static EmailDefaults Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();
        var settings = EmailSettings.Read(configuration.GetSection(SectionName), SectionName, problems);
        return problems.Count > 0
            ? throw new InvalidOperationException($"The mail configuration is invalid. {string.Join(" ", problems)}")
            : new EmailDefaults(settings);
    }
}
