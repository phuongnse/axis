using System.Reflection;
using System.Text.RegularExpressions;
using Axis.Configuration.Compilation;

namespace Axis.Configuration.Tests;

/// <summary>
/// The names that carry <c>labelKey</c>, as <c>docs/reference/authoring.md</c> lists them, are the lists of
/// <see cref="DefinedNames"/> whose items define a <c>LabelKey</c>.
/// </summary>
public sealed class DefinedNamesDocumentationTests
{
    private const string BulletStart = "- **`labelKey`**. ";

    private static readonly string[] _notNames = ["labelKey", "label", "title"];

    [Fact]
    public void The_lists_the_doc_says_carry_a_label_key_are_the_lists_whose_items_define_one()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "authoring.md"));
        var starts = lines.Where(line => line.StartsWith("- **`labelKey`**", StringComparison.Ordinal)).ToList();
        Assert.True(starts.Count == 1, $"Expected one bullet that starts with '- **`labelKey`**', found {starts.Count}.");

        var start = Array.IndexOf(lines, starts[0]);
        var bullet = string.Join(' ', lines.Skip(start).TakeWhile((line, index) => index == 0 || line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line.Trim()));
        var sentence = bullet[BulletStart.Length..];
        sentence = sentence[..(sentence.IndexOf(". ", StringComparison.Ordinal) + 1)];

        var documented = Regex.Matches(sentence, "`([^`]+)`")
            .Select(match => match.Groups[1].Value)
            .Except(_notNames, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var defined = new SortedSet<string>(StringComparer.Ordinal);
        CollectLabelledLists(typeof(DefinedNames), defined);

        Assert.Equal(defined, documented);
    }

    private static void CollectLabelledLists(Type type, ISet<string> names)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ListItemType(property.PropertyType) is not { } item || item.Namespace != typeof(DefinedNames).Namespace)
            {
                continue;
            }

            if (item.GetProperty("LabelKey") is not null)
            {
                names.Add(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]);
            }

            CollectLabelledLists(item, names);
        }
    }

    private static Type? ListItemType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;

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
