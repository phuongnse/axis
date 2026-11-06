using System.Text.RegularExpressions;
using Axis.Configuration.Releases;
using Axis.Server.Studio;

namespace Axis.Server.Tests;

/// <summary>
/// The words of a relaxed point come from the module files that a release stores, and a part without a label shows its
/// name. The kinds of point that <c>docs/reference/authoring.md</c> lists are the ones the code answers.
/// </summary>
public sealed partial class StudioRelaxedWordsTests
{
    private const string Contract = """
        {
          "id": "5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71", "kind": "contract", "name": "RequestContract", "formatVersion": 1, "entity": "Request",
          "lifecycle": {
            "field": "status",
            "states": [
              { "name": "draft", "label": { "textKey": "requests.state.draft" }, "initial": true, "deletable": true },
              { "name": "sent" }
            ],
            "transitions": [
              { "name": "submit", "label": { "textKey": "requests.transition.submit" }, "from": ["draft"], "to": "sent", "guard": "amount > 0" },
              { "name": "plain", "from": ["sent"], "to": "draft", "guard": "amount > 0" }
            ]
          },
          "validations": [{ "name": "frozen", "label": { "textKey": "requests.validation.frozen" }, "expression": "amount > 0" }],
          "effects": [{ "name": "recorded", "label": { "textKey": "requests.effect.recorded" }, "on": "submit" }],
          "ceiling": { "slots": { "requester": [{ "actions": ["read"] }], "unlabelled": [{ "actions": ["read"] }] }, "others": [{ "actions": ["read"] }] }
        }
        """;

    private const string Module = """
        {
          "kind": "module", "id": "requests", "version": "1.0.0", "label": "Requests",
          "roles": [{ "name": "requester", "label": { "textKey": "requests.role.requester" } }, { "name": "unlabelled", "label": { "textKey": "requests.role.missing" } }]
        }
        """;

    private const string LabelledEntity = """
        { "id": "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a01", "kind": "entity", "name": "Request", "formatVersion": 1, "label": { "textKey": "requests.entity.request" }, "fields": [] }
        """;

    private const string UnlabelledEntity = """
        { "id": "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a01", "kind": "entity", "name": "Request", "formatVersion": 1, "fields": [] }
        """;

    private const string English = """
        {
          "id": "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a09", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": {
            "requests.entity.request": "Request", "requests.state.draft": "Draft", "requests.transition.submit": "Submit",
            "requests.validation.frozen": "Amount frozen", "requests.effect.recorded": "Decision recorded", "requests.role.requester": "Requester"
          }
        }
        """;

    private const string Vietnamese = """
        {
          "id": "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a0a", "kind": "text", "name": "TextsVi", "formatVersion": 1, "locale": "vi",
          "texts": { "requests.transition.submit": "Gửi" }
        }
        """;

    [Theory]
    [InlineData("Request/transitions/submit", "transition", "Submit")]
    [InlineData("Request/states/draft/initial", "initial", "Draft")]
    [InlineData("Request/states/draft/deletable", "deletable", "Draft")]
    [InlineData("Request/validations/frozen", "validation", "Amount frozen")]
    [InlineData("Request/effects/recorded", "effect", "Decision recorded")]
    [InlineData("Request/ceiling/requester", "ceiling", "Requester")]
    public void A_labelled_point_has_its_kind_its_labels_and_the_module_and_entity_labels(string point, string part, string title)
    {
        var relaxed = Assert.Single(StudioRelaxedWords.Read(Manifest(point), Copies(LabelledEntity, English)));

        Assert.Equal(("requests", point, "Why."), (relaxed.Module, relaxed.Point, relaxed.Reason));
        Assert.Equal(("Requests", part), (relaxed.ModuleTitle, relaxed.Part));
        Assert.Equal(title, relaxed.Titles!["en"]);
        Assert.Equal("Request", relaxed.EntityTitles!["en"]);
    }

    [Fact]
    public void A_label_is_read_in_every_locale_that_has_its_text()
    {
        var relaxed = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/transitions/submit"), Copies(LabelledEntity, English, Vietnamese)));

        Assert.Equal(new Dictionary<string, string> { ["en"] = "Submit", ["vi"] = "Gửi" }, relaxed.Titles);
    }

    [Fact]
    public void An_unlabelled_point_and_entity_show_their_names_in_each_locale_of_the_module_texts()
    {
        var copies = Copies(UnlabelledEntity, English, Vietnamese);

        var transition = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/transitions/plain"), copies));
        var state = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/states/sent/initial"), copies));

        Assert.Equal(new Dictionary<string, string> { ["en"] = "plain", ["vi"] = "plain" }, transition.Titles);
        Assert.Equal(new Dictionary<string, string> { ["en"] = "Request", ["vi"] = "Request" }, transition.EntityTitles);
        Assert.Equal(new Dictionary<string, string> { ["en"] = "sent", ["vi"] = "sent" }, state.Titles);
    }

    [Fact]
    public void Names_stand_in_for_en_when_the_module_has_no_texts()
    {
        var relaxed = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/transitions/plain"), Copies(UnlabelledEntity)));

        Assert.Equal(new Dictionary<string, string> { ["en"] = "plain" }, relaxed.Titles);
        Assert.Equal(new Dictionary<string, string> { ["en"] = "Request" }, relaxed.EntityTitles);
    }

    [Fact]
    public void A_slot_whose_role_label_has_no_text_shows_the_slot_name_and_the_other_roles_have_no_titles()
    {
        var copies = Copies(LabelledEntity, English);

        var slot = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/ceiling/unlabelled"), copies));
        var others = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/ceiling/others"), copies));

        Assert.Equal(new Dictionary<string, string> { ["en"] = "unlabelled" }, slot.Titles);
        Assert.Equal(("ceiling", null), (others.Part, others.Titles));
        Assert.Equal("Request", others.EntityTitles!["en"]);
    }

    [Fact]
    public void A_point_the_contract_lacks_has_the_module_and_entity_words_but_no_part_and_no_titles()
    {
        var relaxed = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/transitions/gone"), Copies(LabelledEntity, English)));

        Assert.Equal("Requests", relaxed.ModuleTitle);
        Assert.Equal("Request", relaxed.EntityTitles!["en"]);
        Assert.Null(relaxed.Part);
        Assert.Null(relaxed.Titles);
    }

    [Fact]
    public void A_release_without_a_copy_of_the_module_has_no_words()
    {
        var relaxed = Assert.Single(StudioRelaxedWords.Read(Manifest("Request/transitions/submit"), []));

        Assert.Equal(("requests", "Request/transitions/submit", "Why."), (relaxed.Module, relaxed.Point, relaxed.Reason));
        Assert.All([relaxed.ModuleTitle, relaxed.Part], Assert.Null);
        Assert.All([relaxed.EntityTitles, relaxed.Titles], Assert.Null);
    }

    [Fact]
    public void A_missing_manifest_or_one_without_relax_entries_has_no_points()
    {
        Assert.Empty(StudioRelaxedWords.Read(null, []));
        Assert.Empty(StudioRelaxedWords.Read(
            """{ "id": "8d1f0c1e-5a3b-4b7e-9c2d-1a2b3c4d5e6f", "kind": "application", "name": "App", "formatVersion": 1, "modules": [{ "id": "requests", "version": "^1.0.0" }] }""",
            []));
    }

    [Fact]
    public void The_kinds_of_point_in_the_doc_are_the_kinds_in_the_code()
    {
        var bullet = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "authoring.md"))
            .SkipWhile(line => !line.StartsWith("- **Words for relaxed points", StringComparison.Ordinal))
            .TakeWhile((line, index) => index == 0 || line.StartsWith("  ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(bullet);

        var list = PartList().Match(string.Join(' ', bullet.Select(line => line.Trim())));
        Assert.True(list.Success, "The bullet says what `part` is in a list of kinds in backticks.");
        var documented = Backticked().Matches(list.Groups[1].Value).Select(match => match.Groups[1].Value);

        Assert.Equal([.. StudioRelaxedWords.Parts.Order(StringComparer.Ordinal)], [.. documented.Order(StringComparer.Ordinal)]);
    }

    private static string Manifest(string point) =>
        $$"""
        {
          "id": "8d1f0c1e-5a3b-4b7e-9c2d-1a2b3c4d5e6f", "kind": "application", "name": "App", "formatVersion": 1,
          "modules": [{ "id": "requests", "version": "^1.0.0", "relax": [{ "point": "{{point}}", "reason": "Why." }] }]
        }
        """;

    private static List<ResourceContent> Copies(string entity, params string[] texts) =>
    [
        new("modules/requests/module.json", Module),
        new("modules/requests/entities/request.json", entity),
        new("modules/requests/contracts/request.json", Contract),
        .. texts.Select((text, index) => new ResourceContent($"modules/requests/texts/{index}.json", text)),
    ];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    // "`part` is `transition`, `initial` or `ceiling`": the kinds of point after `part` is, each in backticks.
    [GeneratedRegex("`part` is ((?:`[a-z]+`(?:, | or )?)+)")]
    private static partial Regex PartList();

    [GeneratedRegex("`([a-z]+)`")]
    private static partial Regex Backticked();
}
