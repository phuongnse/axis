using System.Text.Json.Nodes;

namespace Axis.Configuration.Tests;

/// <summary>
/// Keeps every widget, action, column and tab of a page named, so a customization layer can find it.
/// </summary>
public sealed class PageElementNameTests
{
    private static readonly string[] _roots =
    [
        "samples/apps",
        "samples/templates",
        "samples/modules",
        "tests/Axis.Configuration.Tests/Fixtures",
        "tests/Axis.Integration.Tests/Fixtures",
        "tests/e2e/fixtures",
    ];

    private static readonly string[] _itemLists = ["actions", "rowActions", "toolbarActions"];

    [Fact]
    public void Every_page_element_in_the_samples_templates_modules_and_fixtures_has_a_name()
    {
        var root = RepositoryRoot();
        var pages = _roots
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.json", SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(Path.GetDirectoryName(file)) == "pages")
            .Order()
            .ToList();
        Assert.NotEmpty(pages);

        var problems = new List<string>();
        foreach (var file in pages)
        {
            var page = JsonNode.Parse(File.ReadAllText(file))!;
            if (page["kind"]?.GetValue<string>() != "page")
            {
                continue;
            }

            problems.AddRange(UnnamedElements(page, Path.GetRelativePath(root, file).Replace('\\', '/')));
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void An_unnamed_tab_in_an_inline_page_is_reported_with_its_pointer()
    {
        var page = JsonNode.Parse("""
            {
              "id": "7d1c2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f",
              "kind": "page",
              "name": "Inline",
              "formatVersion": 1,
              "title": { "textKey": "pages.inline.title" },
              "widgets": [
                {
                  "type": "tabs",
                  "name": "tabs",
                  "tabs": [
                    { "name": "tab1", "label": { "textKey": "tabs.one" }, "widgets": [{ "type": "table", "name": "table", "entity": "Note" }] },
                    { "label": { "textKey": "tabs.two" }, "widgets": [{ "type": "table", "name": "table2", "entity": "Note" }] }
                  ]
                }
              ]
            }
            """)!;

        Assert.Equal(["inline: /widgets/0/tabs/1 has no name"], UnnamedElements(page, "inline"));
    }

    [Fact]
    public void A_page_written_to_a_temporary_folder_with_an_unnamed_element_is_rejected()
    {
        using var folder = new TemporaryFolder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            folder.With("pages/p.json", """
                {
                  "id": "7d1c2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f",
                  "kind": "page",
                  "name": "Inline",
                  "formatVersion": 1,
                  "title": { "textKey": "pages.inline.title" },
                  "widgets": [
                    {
                      "type": "table",
                      "name": "table",
                      "entity": "Note",
                      "rowActions": [{ "type": "navigate", "label": { "textKey": "actions.open" }, "to": "Note" }]
                    }
                  ]
                }
                """);
        });

        Assert.Contains("/widgets/0/rowActions/0 has no name", exception.Message);
    }

    internal static List<string> UnnamedElements(JsonNode page, string file)
    {
        var problems = new List<string>();
        if (page["widgets"] is JsonArray widgets)
        {
            CheckWidgets(widgets, "/widgets", file, problems);
        }

        return problems;
    }

    private static void CheckWidgets(JsonArray widgets, string pointer, string file, List<string> problems)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            if (widgets[i] is not JsonObject widget)
            {
                continue;
            }

            var widgetPointer = $"{pointer}/{i}";
            CheckNamed(widget, widgetPointer, file, problems);
            foreach (var list in _itemLists)
            {
                CheckItems(widget, list, widgetPointer, file, problems);
            }

            if (widget["widgets"] is JsonArray children)
            {
                CheckWidgets(children, $"{widgetPointer}/widgets", file, problems);
            }

            CheckContainers(widget, "columns", widgetPointer, file, problems);
            CheckContainers(widget, "tabs", widgetPointer, file, problems);
        }
    }

    private static void CheckContainers(JsonObject widget, string key, string pointer, string file, List<string> problems)
    {
        if (widget[key] is not JsonArray containers)
        {
            return;
        }

        for (var i = 0; i < containers.Count; i++)
        {
            if (containers[i] is not JsonObject container)
            {
                continue;
            }

            var containerPointer = $"{pointer}/{key}/{i}";
            CheckNamed(container, containerPointer, file, problems);
            if (container["widgets"] is JsonArray children)
            {
                CheckWidgets(children, $"{containerPointer}/widgets", file, problems);
            }
        }
    }

    private static void CheckItems(JsonObject widget, string key, string pointer, string file, List<string> problems)
    {
        if (widget[key] is not JsonArray items)
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is JsonObject item)
            {
                CheckNamed(item, $"{pointer}/{key}/{i}", file, problems);
            }
        }
    }

    private static void CheckNamed(JsonObject element, string pointer, string file, List<string> problems)
    {
        if (element["name"] is null)
        {
            problems.Add($"{file}: {pointer} has no name");
        }
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
