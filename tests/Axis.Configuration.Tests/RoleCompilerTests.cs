using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class RoleCompilerTests
{
    private const string FinanceId = "44444444-4444-4444-8444-444444444401";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "role.finance": "Finance" } }
        """;

    [Fact]
    public void A_role_compiles_into_the_model_and_the_release_resources()
    {
        using var folder = Folder().With("roles/finance.json", Role(FinanceId, "finance"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var role = Assert.Single(result.Model.Roles);
        Assert.Equal(
            (Guid.Parse(FinanceId), "finance", new TextReference("role.finance"), "roles/finance.json"),
            (role.Id, role.Name, role.Label, role.File));
        Assert.Contains(result.Resources, resource => resource.Path == "roles/finance.json");
    }

    [Fact]
    public void Two_roles_with_the_same_name_are_a_duplicate_name_at_the_later_file_and_give_no_model()
    {
        using var folder = Folder()
            .With("roles/a-finance.json", Role(FinanceId, "finance"))
            .With("roles/b-finance.json", Role("44444444-4444-4444-8444-444444444402", "finance"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateName, "roles/b-finance.json", "/name", "The role name 'finance' is already used by 'roles/a-finance.json'."),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_role_label_with_no_text_in_any_locale_is_a_missing_text()
    {
        using var folder = Folder().With("roles/finance.json", Role(FinanceId, "finance", textKey: "role.nope"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTextKey, "roles/finance.json", "/label/textKey"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "finance", "formatVersion": 1 }""")]
    [InlineData("""{ "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "Finance", "formatVersion": 1, "label": { "textKey": "role.finance" } }""")]
    [InlineData("""{ "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "finance_reviewer", "formatVersion": 1, "label": { "textKey": "role.finance" } }""")]
    [InlineData("""{ "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "1finance", "formatVersion": 1, "label": { "textKey": "role.finance" } }""")]
    [InlineData("""{ "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "finance", "formatVersion": 1, "label": { "textKey": "role.finance" }, "grants": [] }""")]
    public void A_role_without_a_label_or_with_a_name_in_another_form_is_a_schema_violation(string role)
    {
        using var folder = Folder().With("roles/finance.json", role);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "roles/finance.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_role_name_longer_than_60_characters_is_a_schema_violation()
    {
        using var folder = Folder().With("roles/finance.json", Role(FinanceId, new string('a', 61)));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "/name"), (diagnostic.Code, diagnostic.Path));
    }

    private static string Role(string id, string name, string textKey = "role.finance") => $$"""
        { "id": "{{id}}", "kind": "role", "name": "{{name}}", "formatVersion": 1, "label": { "textKey": "{{textKey}}" } }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts);
}
