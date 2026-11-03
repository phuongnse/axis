using Axis.Configuration.Templates;
using Microsoft.Extensions.Logging;

namespace Axis.Configuration.Tests;

public sealed class TemplateCatalogTests
{
    private const string Site = """{ "kind": "site", "name": "Blank" }""";

    [Fact]
    public void An_installed_template_loads_with_its_manifest_and_files_without_template_json()
    {
        using var root = Template(new TemporaryFolder(), "request-approval", "1.0.0")
            .With("request-approval/1.0.0/entities/Request.json", """{ "kind": "entity" }""")
            .With("request-approval/1.0.0/readme.txt", "ignored");

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Problems);
        var template = Assert.Single(catalog.Newest);
        Assert.Equal(
            new TemplateManifest(
                "request-approval",
                "1.0.0",
                new TemplateText("Title of request-approval", "Tiêu đề của request-approval"),
                new TemplateText("Description of request-approval", "Mô tả của request-approval")),
            template.Manifest);
        Assert.Equal(["entities/Request.json", "sites/site.json"], template.Files.Select(file => file.Path));
    }

    [Fact]
    public void A_manifest_with_built_in_true_loads_marked_as_built_in()
    {
        using var root = new TemporaryFolder()
            .With("tpl-a/1.0.0/template.json", """{ "kind": "applicationTemplate", "id": "tpl-a", "version": "1.0.0", "title": { "en": "T", "vi": "T" }, "description": { "en": "D", "vi": "D" }, "builtIn": true }""")
            .With("tpl-a/1.0.0/sites/site.json", Site);

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Problems);
        Assert.True(Assert.Single(catalog.Newest).Manifest.BuiltIn);
    }

    [Fact]
    public void A_built_in_flag_that_is_not_a_boolean_leaves_the_template_out()
    {
        using var root = new TemporaryFolder()
            .With("tpl-a/1.0.0/template.json", """{ "kind": "applicationTemplate", "id": "tpl-a", "version": "1.0.0", "title": { "en": "T", "vi": "T" }, "description": { "en": "D", "vi": "D" }, "builtIn": "yes" }""")
            .With("tpl-a/1.0.0/sites/site.json", Site);

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Newest);
        Assert.Equal("tpl-a/1.0.0", Assert.Single(catalog.Problems).Folder);
    }

    [Fact]
    public void The_newest_version_of_each_template_is_listed_by_id()
    {
        using var root = new TemporaryFolder();
        Template(root, "tpl-b", "1.0.0");
        Template(root, "tpl-a", "1.10.0");
        Template(root, "tpl-a", "1.2.0");

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Problems);
        Assert.Equal(
            [("tpl-a", "1.10.0"), ("tpl-b", "1.0.0")],
            catalog.Newest.Select(template => (template.Manifest.Id, template.Manifest.Version)));
    }

    [Theory]
    [InlineData("Bad_Id", "1.0.0")]
    [InlineData("tpl-a", "1.0")]
    [InlineData("tpl-a", "v1.0.0")]
    public void A_bad_id_or_version_leaves_the_template_out_and_the_others_still_load(string id, string version)
    {
        using var root = new TemporaryFolder();
        Template(root, "tpl-ok", "1.0.0");
        Template(root, "tpl-bad", "2.0.0", manifestId: id, manifestVersion: version);

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Equal("tpl-ok", Assert.Single(catalog.Newest).Manifest.Id);
        var problem = Assert.Single(catalog.Problems);
        Assert.Equal("tpl-bad/2.0.0", problem.Folder);
        Assert.DoesNotContain(root.Path, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_names_that_differ_from_the_manifest_leave_the_template_out()
    {
        using var root = new TemporaryFolder();
        Template(root, "tpl-a", "1.0.0", manifestVersion: "1.0.1");
        Template(root, "tpl-b", "1.0.0", manifestId: "tpl-c");

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Newest);
        Assert.Equal(["tpl-a/1.0.0", "tpl-b/1.0.0"], catalog.Problems.Select(problem => problem.Folder));
    }

    [Fact]
    public void A_missing_or_invalid_manifest_leaves_the_template_out()
    {
        using var root = new TemporaryFolder()
            .With("tpl-a/1.0.0/sites/site.json", Site)
            .With("tpl-b/1.0.0/template.json", "{")
            .With("tpl-b/1.0.0/sites/site.json", Site)
            .With("tpl-c/1.0.0/template.json", """{ "kind": "applicationTemplate", "id": "tpl-c", "version": "1.0.0", "title": { "en": "T" }, "description": { "en": "D", "vi": "D" } }""")
            .With("tpl-c/1.0.0/sites/site.json", Site);

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Newest);
        Assert.Equal(["tpl-a/1.0.0", "tpl-b/1.0.0", "tpl-c/1.0.0"], catalog.Problems.Select(problem => problem.Folder));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_template_without_exactly_one_site_is_left_out(int sites)
    {
        using var root = new TemporaryFolder().With("tpl-a/1.0.0/entities/Request.json", """{ "kind": "entity" }""");
        Template(root, "tpl-a", "1.0.0", siteCount: sites);

        var catalog = TemplateCatalog.Load(root.Path);

        Assert.Empty(catalog.Newest);
        var problem = Assert.Single(catalog.Problems);
        Assert.Equal("tpl-a/1.0.0", problem.Folder);
        Assert.Contains("one site", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_or_empty_root_gives_an_empty_catalog()
    {
        Assert.Empty(TemplateCatalog.Load(null).Newest);
        Assert.Empty(TemplateCatalog.Load("").Newest);
    }

    [Fact]
    public void A_missing_root_throws_with_the_setting_name()
    {
        using var root = new TemporaryFolder();

        var exception = Assert.Throws<InvalidOperationException>(() => TemplateCatalog.Load(Path.Combine(root.Path, "missing")));

        Assert.Contains("Templates:Path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_problem_is_logged_once_at_start_as_a_warning_that_names_its_folder()
    {
        using var root = new TemporaryFolder();
        Template(root, "tpl-ok", "1.0.0");
        Template(root, "tpl-bad", "1.0.0", manifestId: "Bad_Id");
        var logger = new RecordingLogger();
        var hosted = new TemplateDiagnosticsLogger(TemplateCatalog.Load(root.Path), logger);

        await hosted.StartAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("tpl-bad/1.0.0", entry.Message, StringComparison.Ordinal);
    }

    private static TemporaryFolder Template(
        TemporaryFolder root,
        string id,
        string version,
        string? manifestId = null,
        string? manifestVersion = null,
        int siteCount = 1)
    {
        var prefix = $"{id}/{version}/";
        root.With(prefix + "template.json", $$"""
            {
              "kind": "applicationTemplate",
              "id": "{{manifestId ?? id}}",
              "version": "{{manifestVersion ?? version}}",
              "title": { "en": "Title of {{id}}", "vi": "Tiêu đề của {{id}}" },
              "description": { "en": "Description of {{id}}", "vi": "Mô tả của {{id}}" }
            }
            """);
        for (var index = 0; index < siteCount; index++)
        {
            root.With(prefix + (index == 0 ? "sites/site.json" : $"sites/site{index}.json"), Site);
        }

        return root;
    }

    private sealed class RecordingLogger : ILogger<TemplateDiagnosticsLogger>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
