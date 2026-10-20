namespace Axis.Integration.Tests;

/// <summary>
/// The sample templates that the test project copies to the test output, laid out as they are installed:
/// <c>request-approval/1.0.0</c>. The test project's build does this, so the tests do not need
/// <c>scripts/build.sh</c> first.
/// </summary>
internal static class SampleTemplates
{
    /// <summary>The folder that acts as <c>Templates:Path</c>.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "SampleTemplates");
}
