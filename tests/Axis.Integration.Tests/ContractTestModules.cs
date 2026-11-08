namespace Axis.Integration.Tests;

/// <summary>
/// Copies of the test modules for the publisher tests that run contract scenarios. The shared fixture is used by other tests, so a test
/// that needs a contract scenario of the <c>requests</c> module gets its own copy.
/// </summary>
internal static class ContractTestModules
{
    // The shared test modules, without any contract scenario of their own.
    internal static readonly string Modules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");

    // A copy of the test modules whose requests module has a contract scenario. It submits a draft request, and then expects the
    // given status, so "approved" makes it fail at its last step.
    internal static TempModules ModulesWithScenario(string expected)
    {
        var copy = new TempModules();
        CopyFolder(Modules, copy.Path);
        var folder = Path.Combine(copy.Path, "requests", "1.0.0", "scenarios");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "submit-request.json"),
            $$"""
            {
              "id": "6b8e1d24-7c35-4a90-b1f2-3d5e7a9c0e11",
              "kind": "scenario",
              "name": "SubmitRequest",
              "formatVersion": 1,
              "users": { "requester": ["requester"] },
              "contract": { "covers": ["Request/transitions/submit"] },
              "steps": [
                { "type": "createRecord", "entity": "Request", "as": "requester", "save": "request", "values": { "title": "Chairs", "amount": 5, "status": "draft" } },
                { "type": "updateRecord", "record": "request", "as": "requester", "values": { "status": "submitted" } },
                { "type": "expectRecord", "record": "request", "values": { "status": "{{expected}}" } }
              ]
            }
            """);
        return copy;
    }

    internal static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }
}

internal sealed class TempModules : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"axis-modules-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
