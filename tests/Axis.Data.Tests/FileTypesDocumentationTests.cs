using Axis.Data.Files;

namespace Axis.Data.Tests;

/// <summary>
/// Compares the type table in <c>docs/reference/file-fields.md</c> with <see cref="FileSignatures.Types"/>, so a media
/// type that an upload may have, and the extensions that belong to it, are documented.
/// </summary>
public sealed class FileTypesDocumentationTests
{
    [Fact]
    public void The_type_table_names_exactly_the_media_types_and_extensions_that_uploads_are_checked_for()
    {
        var documented = Table().ToDictionary(row => row.MediaType, row => row.Extensions, StringComparer.Ordinal);

        Assert.Equal(FileSignatures.Types.Keys.Order(StringComparer.Ordinal), documented.Keys.Order(StringComparer.Ordinal));
        foreach (var (mediaType, type) in FileSignatures.Types)
        {
            Assert.Equal(type.Extensions.Order(StringComparer.Ordinal), documented[mediaType].Order(StringComparer.Ordinal));
        }
    }

    // The rows of the table, which sits in a list item, whose header starts with Media type: the media type in code font, and its extensions in the second cell.
    private static List<(string MediaType, string[] Extensions)> Table()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "file-fields.md")).Select(line => line.TrimStart()).ToArray();
        var headers = lines.Select((line, index) => (line, index)).Where(entry => entry.line.StartsWith("| Media type ", StringComparison.Ordinal)).ToList();
        Assert.True(headers.Count == 1, $"Expected one table with a Media type header, found {headers.Count}.");

        var rows = new List<(string, string[])>();
        foreach (var line in lines.Skip(headers[0].index + 2).TakeWhile(line => line.StartsWith('|')))
        {
            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            rows.Add((cells[0].Trim('`'), cells[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(extension => extension.Trim('`')).ToArray()));
        }

        return rows;
    }

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
