namespace Axis.Integration.Tests;

/// <summary>
/// The built-in applications that the test project copies to the test output, laid out as they are installed:
/// <c>approvals/1.0.0</c>. The test project's build does this, so the tests do not need
/// <c>scripts/build.sh</c> first.
/// </summary>
internal static class BuiltInApplications
{
    /// <summary>The folder that acts as <c>Templates:Path</c>.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "BuiltInApplications");
}
