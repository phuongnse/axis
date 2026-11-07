using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.Server.Tests;

/// <summary>
/// The <c>Route</c> tables and route headings under <c>docs/</c> list exactly the routes of the server's OpenAPI document,
/// so a route added or removed on one side without the other fails here. See <c>docs/writing-docs.md#how-the-docs-stay-correct</c>.
/// </summary>
public sealed partial class RouteTableDocTests
{
    [Fact]
    public void The_route_tables_and_headings_of_the_docs_list_the_routes_of_the_OpenAPI_document()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, OpenApiHost.DocumentPath);
        Assert.True(File.Exists(path), $"{OpenApiHost.DocumentPath} is missing. Run: dotnet run --project tests/Axis.OpenApiDocument -c Release");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var described = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(route => route.Value.EnumerateObject().Select(operation => $"{operation.Name.ToUpperInvariant()} {Normalize(route.Name)}"))
            .ToHashSet(StringComparer.Ordinal);
        var documented = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .SelectMany(file => DocumentedRoutes(File.ReadAllLines(file))
                .Select(route => (route.Route, Where: $"{Path.GetRelativePath(root, file)}:{route.Line}")))
            .ToList();
        Assert.NotEmpty(described);
        Assert.NotEmpty(documented);

        var differences = Differences(described, documented);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void The_reader_takes_routes_from_headings_and_from_a_Route_column_in_any_position()
    {
        string[] lines =
        [
            "## `GET /api/x/{id}`",
            "",
            "## `DELETE /api/x/{id}`",
            "_(planned for later)_",
            "",
            "  | Group | Route |",
            "  | ----- | ----- |",
            "  | Items | `POST /api/y?page=` |",
            "",
            "| Stub route | Effect |",
            "| ---------- | ------ |",
            "| `POST /test/fail` | Stub only. |",
            "",
            "| Route | What it does |",
            "| ----- | ------------ |",
            "| `GET /api/planned` _(planned for later)_ | Not mapped yet. |",
            "| `GET /api/z/` | Trailing slash is dropped. |",
            "| Create application | Plain text. |",
        ];

        string[] expected = ["GET /api/x/{}", "POST /api/y", "GET /api/z"];
        Assert.Equal(expected, DocumentedRoutes(lines).Select(route => route.Route).ToArray());
    }

    [Fact]
    public void A_route_on_one_side_only_is_named_in_the_differences()
    {
        var differences = Differences(
            new HashSet<string>(["GET /api/a", "GET /api/b"], StringComparer.Ordinal),
            [("GET /api/a", "docs/x.md:3"), ("GET /api/c", "docs/y.md:9")]);

        string[] expected =
        [
            "GET /api/b is in the OpenAPI document but in no Route table or route heading under docs/",
            "GET /api/c at docs/y.md:9 is not in the OpenAPI document",
        ];
        Assert.Equal(expected, differences);
    }

    internal static IEnumerable<(string Route, int Line)> DocumentedRoutes(IReadOnlyList<string> lines)
    {
        var routeColumn = -1;
        var rowInTable = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].TrimStart();
            if (!line.StartsWith('|'))
            {
                rowInTable = -1;
                if (RouteHeading().Match(line) is { Success: true } heading && !IsPlannedHeading(lines, index))
                {
                    yield return (RouteKey(heading.Groups["method"].Value, heading.Groups["path"].Value), index + 1);
                }

                continue;
            }

            rowInTable++;
            if (rowInTable == 0)
            {
                routeColumn = Array.FindIndex(line.Split('|'), cell => cell.Trim() == "Route");
                continue;
            }

            if (rowInTable == 1 || routeColumn < 0 || line.Contains("_(planned for ", StringComparison.Ordinal))
            {
                continue;
            }

            var cell = line.Split('|')[routeColumn];
            foreach (Match span in RouteSpan().Matches(cell))
            {
                yield return (RouteKey(span.Groups["method"].Value, span.Groups["path"].Value), index + 1);
            }
        }
    }

    internal static List<string> Differences(ISet<string> described, IEnumerable<(string Route, string Where)> documented)
    {
        var documentedRoutes = documented.Select(entry => entry.Route).ToHashSet(StringComparer.Ordinal);
        var missing = described.Where(route => !documentedRoutes.Contains(route))
            .Select(route => $"{route} is in the OpenAPI document but in no Route table or route heading under docs/");
        var unknown = documented.Where(entry => !described.Contains(entry.Route))
            .Select(entry => $"{entry.Route} at {entry.Where} is not in the OpenAPI document");
        return missing.Concat(unknown).Order(StringComparer.Ordinal).ToList();
    }

    internal static string Normalize(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        var withoutQuery = query < 0 ? path : path[..query];
        var trimmed = ParameterSpan().Replace(withoutQuery, "{}").TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string RouteKey(string method, string path) => $"{method} {Normalize(path)}";

    private static bool IsPlannedHeading(IReadOnlyList<string> lines, int index)
    {
        var next = index + 1;
        while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
        {
            next++;
        }

        return next < lines.Count && lines[next].Contains("_(planned for ", StringComparison.Ordinal);
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

    // A heading made of a backticked route, such as "## `GET /api/studio/catalog`".
    [GeneratedRegex(@"^#{1,6} `(?<method>GET|POST|PUT|PATCH|DELETE|HEAD) (?<path>/[^`]*)`\s*$")]
    private static partial Regex RouteHeading();

    // A backticked route in a Route cell, such as "`GET /api/studio/catalog`".
    [GeneratedRegex(@"`(?<method>GET|POST|PUT|PATCH|DELETE|HEAD) (?<path>/\S*)`")]
    private static partial Regex RouteSpan();

    // A parameter such as {exportId:guid} or {*path} is written {} on both sides of the comparison.
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex ParameterSpan();
}
