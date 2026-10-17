namespace Axis.Worker;

/// <summary>Where the worker serves its health endpoints, bound from the <c>Health</c> configuration section.</summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    /// <summary>The addresses of <c>/health/live</c> and <c>/health/ready</c>, separated by semicolons.</summary>
    public string Urls { get; set; } = "http://127.0.0.1:5208";
}
