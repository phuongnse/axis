using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class SiteSignInCompilerTests
{
    private const string CompanyId = "5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e62";
    private const string FirstSiteId = "66666666-6666-4666-8666-666666666671";
    private const string SecondSiteId = "66666666-6666-4666-8666-666666666672";

    private const string Texts = """
        { "id": "55555555-5555-4555-8555-555555555555", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Home", "nav.home": "Home", "pages.home.title": "Home", "signIn.company": "Continue with Company" } }
        """;

    private const string Company = """
        "type": "oidc", "label": { "textKey": "signIn.company" },
        "issuer": { "binding": "CompanyIssuer" },
        "clientId": { "binding": "CompanyClientId" },
        "clientSecret": { "binding": "CompanyClientSecret" }
        """;

    [Fact]
    public void A_sign_in_entry_that_names_no_provider_is_reported_at_its_index()
    {
        using var folder = Folder(Site(FirstSiteId, "First", "first", """ "signIn": ["accounts", "missing"], """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownSignInProvider, "sites/first.json", "/signIn/1"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Each_site_of_an_application_without_a_provider_is_reported_at_its_root()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("pages/home.json", Home)
            .With("sites/first.json", Site(FirstSiteId, "First", "first", ""))
            .With("sites/second.json", Site(SecondSiteId, "Second", "second", """ "signIn": ["accounts"], """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.SiteWithoutIdentityProvider, "sites/first.json", ""),
                (DiagnosticCodes.SiteWithoutIdentityProvider, "sites/second.json", ""),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_site_without_sign_in_allows_every_provider_and_one_with_sign_in_allows_the_listed_ones()
    {
        using var folder = Folder(
            Site(FirstSiteId, "First", "first", ""),
            Site(SecondSiteId, "Second", "second", """ "signIn": ["COMPANY"], """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var first = Assert.Single(result.Model!.Sites, site => site.Name == "First");
        var second = Assert.Single(result.Model.Sites, site => site.Name == "Second");
        Assert.Equal(["accounts", "company"], first.SignIn.Order().ToArray());
        Assert.Equal(["company"], second.SignIn);
    }

    [Fact]
    public void A_sign_in_entry_that_names_a_provider_with_a_schema_error_is_not_reported_again()
    {
        using var folder = Folder(Site(FirstSiteId, "First", "first", """ "signIn": ["broken"], """))
            .With("identity-providers/broken.json", Provider("5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e63", "broken", """ "label": { "textKey": "signIn.company" } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "identity-providers/broken.json"), (diagnostic.Code, diagnostic.File)));
    }

    [Fact]
    public void A_site_with_only_a_provider_that_fails_the_schema_is_not_reported_for_having_no_provider()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("pages/home.json", Home)
            .With("sites/first.json", Site(FirstSiteId, "First", "first", ""))
            .With("identity-providers/broken.json", Provider("5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e63", "broken", """ "label": { "textKey": "signIn.company" } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "identity-providers/broken.json"), (diagnostic.Code, diagnostic.File)));
    }

    [Theory]
    [InlineData(""" "signIn": [], """, "/signIn")]
    [InlineData(""" "signIn": ["accounts", "accounts"], """, "/signIn")]
    public void An_empty_or_repeating_sign_in_list_breaks_the_schema(string signIn, string path)
    {
        using var folder = Folder(Site(FirstSiteId, "First", "first", signIn));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "sites/first.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    private const string Home = """
        { "id": "55555555-5555-4555-8555-555555555551", "kind": "page", "name": "Home", "formatVersion": 1,
          "title": { "textKey": "pages.home.title" }, "widgets": [ { "type": "taskInbox" } ] }
        """;

    private static string Site(string id, string name, string path, string signIn) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "{{name}}", "formatVersion": 1, "path": "{{path}}", {{signIn}}
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Home", "label": { "textKey": "nav.home" } } ] }
        """;

    private static string Provider(string id, string name, string body) =>
        $$"""
        { "id": "{{id}}", "kind": "identityProvider", "name": "{{name}}", "formatVersion": 1, {{body}} }
        """;

    /// <summary>An application with an <c>accounts</c> and a <c>company</c> provider and the given sites.</summary>
    private static TemporaryFolder Folder(params string[] sites)
    {
        var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("pages/home.json", Home)
            .With("identity-providers/accounts.json", PresentationCompilerTests.AccountsProvider)
            .With("identity-providers/company.json", Provider(CompanyId, "company", Company));
        foreach (var site in sites)
        {
            var path = System.Text.Json.JsonDocument.Parse(site).RootElement.GetProperty("path").GetString();
            folder.With($"sites/{path}.json", site);
        }

        return folder;
    }
}
