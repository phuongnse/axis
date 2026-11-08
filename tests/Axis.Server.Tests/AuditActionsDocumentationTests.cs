using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Data.Audit;

namespace Axis.Server.Tests;

/// <summary>
/// The audit actions that docs/reference/storage.md#audit-actions lists are exactly the <see cref="AuditActions"/> of the code
/// that the reference docs name: every action of the code is named in backticks under docs/reference, and every action name
/// that the section lists is an action of the code.
/// </summary>
public sealed partial class AuditActionsDocumentationTests
{
    [Fact]
    public void Every_audit_action_of_the_code_is_named_in_the_docs()
    {
        var reference = Path.Combine(RepositoryRoot(), "docs", "reference");
        var text = string.Join('\n', Directory.EnumerateFiles(reference, "*.md").Select(File.ReadAllText));

        var missing = Actions().Where(action => !text.Contains($"`{action}`", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, $"AuditActions that no doc under docs/reference names in backticks: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void Every_audit_action_that_the_storage_doc_lists_is_an_action_of_the_code()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "storage.md"));
        var start = Array.FindIndex(lines, line => line == "### Audit actions");
        Assert.True(start >= 0, "docs/reference/storage.md has no '### Audit actions' section.");
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith('#'));
        var section = lines[start..(end < 0 ? lines.Length : end)];

        var listed = section
            .SelectMany(line => ActionName().Matches(line).Select(match => match.Groups[1].Value))
            .Where(name => name != "application.json")
            .Distinct()
            .ToList();
        var unknown = listed.Except(Actions()).ToList();

        Assert.NotEmpty(listed);
        Assert.True(unknown.Count == 0, $"Named in the audit actions section of docs/reference/storage.md but not an AuditActions value: {string.Join(", ", unknown)}.");
    }

    private static List<string> Actions() =>
        [.. typeof(AuditActions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    [GeneratedRegex(@"`([a-z]+\.[a-zA-Z]+)`")]
    private static partial Regex ActionName();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
