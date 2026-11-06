using Microsoft.Extensions.Configuration;

namespace Axis.Hosting;

/// <summary>Where a host serves its health endpoints, read from the <c>Health</c> configuration section.</summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    /// <summary>The addresses of <c>/health/live</c> and <c>/health/ready</c>, separated by semicolons.</summary>
    public required string Urls { get; init; }

    /// <summary>Reads <c>Health:Urls</c>, which is <paramref name="defaultUrls"/> when it is missing.</summary>
    /// <exception cref="InvalidOperationException"><c>Health:Urls</c> is set and blank.</exception>
    public static HealthOptions Load(IConfiguration configuration, string defaultUrls)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var urls = configuration.GetSection(SectionName)[nameof(Urls)] ?? defaultUrls;
        return string.IsNullOrWhiteSpace(urls)
            ? throw new InvalidOperationException($"'{SectionName}:{nameof(Urls)}' must not be empty.")
            : new HealthOptions { Urls = urls };
    }
}
