using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// ModuleLayerApp declares the <c>intake</c> module, whose IntakeForm shows the <c>title</c> field of its
/// <c>Intake</c> entity. The application's layer on that form adds the <c>notes</c> field, so the page
/// <c>pages/intake/NewIntake</c> of its site <c>modulelayer</c> shows both. Its layer on that page adds a
/// <c>review</c> tab <c>after</c> the module's second tab. Its layer on the module's IntakeArchive page removes the page.
/// The module stays as it is.
/// </summary>
public sealed class ModuleLayerTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Page = "/api/sites/modulelayer/pages/intake/NewIntake";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clerk_reads_the_module_form_with_the_field_that_the_layer_adds()
    {
        using var client = fixture.CreateClient().SignedInAs("layer-clerk");

        using var page = await GetAsync(client, Page, HttpStatusCode.OK);

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray(), candidate => candidate.GetProperty("type").GetString() == "form");
        var section = Assert.Single(widget.GetProperty("form").GetProperty("sections").EnumerateArray().ToList());
        Assert.Equal(
            ["title", "notes"],
            section.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_clerk_reads_the_tab_that_the_layer_places_after_the_second_tab_as_the_third()
    {
        using var client = fixture.CreateClient().SignedInAs("layer-clerk");

        using var page = await GetAsync(client, Page, HttpStatusCode.OK);

        var tabs = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray(), candidate => candidate.GetProperty("type").GetString() == "tabs");
        Assert.Equal(
            ["intake.pages.newIntake.details", "intake.pages.newIntake.notes", "intake.pages.newIntake.review", "intake.pages.newIntake.history"],
            tabs.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetProperty("labelKey").GetString()));
    }

    [Fact]
    public async Task A_clerk_gets_404_for_the_page_that_the_layer_removes_and_the_site_does_not_list_it()
    {
        using var client = fixture.CreateClient().SignedInAs("layer-clerk");

        using var missing = await GetAsync(client, "/api/sites/modulelayer/pages/intake/IntakeArchive", HttpStatusCode.NotFound);
        using var site = await GetAsync(client, "/api/sites/modulelayer", HttpStatusCode.OK);

        Assert.Equal("The site's application has no page with this name.", missing.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            ["intake/NewIntake"],
            site.RootElement.GetProperty("navigation").EnumerateArray().Select(entry => entry.GetProperty("page").GetString()));
    }

    [Fact]
    public async Task A_person_without_the_role_gets_403_for_the_page()
    {
        using var client = fixture.CreateClient().SignedInAs("manager");

        using var forbidden = await GetAsync(client, Page, HttpStatusCode.Forbidden);

        Assert.Equal("You have no grant for this action.", forbidden.RootElement.GetProperty("title").GetString());
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, path, ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
