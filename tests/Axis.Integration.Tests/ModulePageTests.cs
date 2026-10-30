using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>departments</c> module holds a Departments page, which ModuleAppA's site <c>modulesa</c> lists
/// in its navigation. The page is read at the two-segment address <c>pages/departments/Departments</c>, and
/// its title comes from the module's texts.
/// </summary>
public sealed class ModulePageTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Site = "/api/sites/modulesa";
    private const string Page = $"{Site}/pages/departments/Departments";
    private const string TitleKey = "departments.pages.departments.title";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_employee_reads_the_module_page_with_its_data_source_and_the_module_texts()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var page = await GetAsync(client, Page, HttpStatusCode.OK);

        Assert.Equal("departments/Departments", page.RootElement.GetProperty("name").GetString());
        Assert.Equal(TitleKey, page.RootElement.GetProperty("titleKey").GetString());
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("table", widget.GetProperty("type").GetString());
        Assert.Equal("departments/Departments", widget.GetProperty("dataSource").GetProperty("name").GetString());

        using var texts = await GetAsync(client, $"{Site}/texts/en", HttpStatusCode.OK);
        Assert.Equal("Departments", texts.RootElement.GetProperty("texts").GetProperty(TitleKey).GetString());
    }

    [Fact]
    public async Task The_site_lists_the_module_page_in_its_navigation_for_an_employee()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var site = await GetAsync(client, Site, HttpStatusCode.OK);

        Assert.Equal(
            ["departments/Departments"],
            site.RootElement.GetProperty("navigation").EnumerateArray().Select(entry => entry.GetProperty("page").GetString()));
    }

    [Fact]
    public async Task A_person_without_a_grant_on_the_module_data_source_gets_403_and_no_navigation_entry()
    {
        using var client = fixture.CreateClient().SignedInAs("manager");

        using var forbidden = await GetAsync(client, Page, HttpStatusCode.Forbidden);

        Assert.Equal("You have no grant for this action.", forbidden.RootElement.GetProperty("title").GetString());
        using var site = await GetAsync(client, Site, HttpStatusCode.OK);
        Assert.Empty(site.RootElement.GetProperty("navigation").EnumerateArray());
    }

    [Theory]
    [InlineData("pages/departments/Missing")]
    [InlineData("pages/other/Departments")]
    [InlineData("pages/Departments")]
    public async Task A_module_page_name_that_does_not_exist_is_a_404_problem(string path)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var problem = await GetAsync(client, $"{Site}/{path}", HttpStatusCode.NotFound);

        Assert.Equal("The site's application has no page with this name.", problem.RootElement.GetProperty("title").GetString());
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, path, ModuleAppFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
