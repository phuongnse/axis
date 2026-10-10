using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class SiteEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_has_the_sites_of_every_active_application_in_the_tenant_with_their_titles()
    {
        using var listInA = await GetJsonAsync("/api/sites", HostA);
        using var listInB = await GetJsonAsync("/api/sites", HostB);

        var sitesInA = listInA.RootElement.GetProperty("sites").EnumerateArray().ToList();
        Assert.Equal(["records", "second"], sitesInA.Select(site => site.GetProperty("path").GetString()));
        var records = sitesInA[0];
        Assert.Equal(["path", "titleKey", "titles"], records.EnumerateObject().Select(property => property.Name));
        Assert.Equal("site.title", records.GetProperty("titleKey").GetString());
        Assert.Equal(
            [("en", "Records"), ("vi", "Hồ sơ")],
            records.GetProperty("titles").EnumerateObject().Select(property => (property.Name, property.Value.GetString())));
        Assert.Equal(
            [("en", "Second")],
            sitesInA[1].GetProperty("titles").EnumerateObject().Select(property => (property.Name, property.Value.GetString())));

        var sitesInB = listInB.RootElement.GetProperty("sites").EnumerateArray().ToList();
        Assert.Equal(["records"], sitesInB.Select(site => site.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task Site_returns_its_title_locales_and_navigation_matching_the_path_ignoring_letter_case()
    {
        using var site = await GetJsonAsync("/api/sites/RECORDS", HostA);

        var root = site.RootElement;
        Assert.Equal(["path", "titleKey", "locales", "navigation"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("records", root.GetProperty("path").GetString());
        Assert.Equal("site.title", root.GetProperty("titleKey").GetString());
        var locales = root.GetProperty("locales");
        Assert.Equal("en", locales.GetProperty("default").GetString());
        Assert.Equal("en", locales.GetProperty("fallback").GetString());
        Assert.Equal(["en", "vi"], locales.GetProperty("available").EnumerateArray().Select(locale => locale.GetString()));
        Assert.Equal(
            [("Items", "nav.items"), ("Departments", "nav.departments")],
            root.GetProperty("navigation").EnumerateArray()
                .Select(entry => (entry.GetProperty("page").GetString(), entry.GetProperty("labelKey").GetString())));
    }

    [Fact]
    public async Task Texts_return_the_application_texts_of_a_locale_matched_ignoring_letter_case()
    {
        using var texts = await GetJsonAsync("/api/sites/records/texts/VI", HostA);

        Assert.Equal("vi", texts.RootElement.GetProperty("locale").GetString());
        Assert.Equal("Hồ sơ", texts.RootElement.GetProperty("texts").GetProperty("site.title").GetString());
        Assert.Equal("Phòng ban", texts.RootElement.GetProperty("texts").GetProperty("item.department").GetString());
    }

    [Fact]
    public async Task Page_returns_its_widgets_with_the_entity_fields_and_record_api_paths()
    {
        using var page = await GetJsonAsync("/api/sites/records/pages/items", HostA);

        var root = page.RootElement;
        Assert.Equal("Items", root.GetProperty("name").GetString());
        Assert.Equal("items.title", root.GetProperty("titleKey").GetString());
        var widget = Assert.Single(root.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("table", widget.GetProperty("type").GetString());
        Assert.Equal("ItemForm", widget.GetProperty("formPage").GetString());

        Assert.Equal(JsonValueKind.Null, widget.GetProperty("dataSource").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("form").ValueKind);

        var entity = widget.GetProperty("entity");
        Assert.Equal("Item", entity.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, entity.GetProperty("labelKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, entity.GetProperty("displayField").ValueKind);
        Assert.Equal("/api/apps/RecordsApp/entities/Item/records", entity.GetProperty("recordsPath").GetString());

        var fieldList = entity.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(
            ["name", "quantity", "price", "active", "neededBy", "orderedAt", "status", "parts", "department", "total"],
            fieldList.Select(field => field.GetProperty("name").GetString()));
        Assert.Equal(
            ["text", "integer", "decimal", "boolean", "date", "date-time", "enum", "child-collection", "reference", "decimal"],
            fieldList.Select(field => field.GetProperty("type").GetString()));
        Assert.All(fieldList, field => Assert.Equal(
            ["name", "type", "labelKey", "required", "unique", "computed", "sequence", "maxLength", "precision", "scale", "values", "target", "fields"],
            field.EnumerateObject().Select(property => property.Name)));

        var fields = fieldList.ToDictionary(field => field.GetProperty("name").GetString()!);

        Assert.Equal(
            """{"name":"name","type":"text","labelKey":null,"required":true,"unique":false,"computed":false,"sequence":false,"maxLength":100,"precision":null,"scale":null,"values":null,"target":null,"fields":null}""",
            fields["name"].GetRawText());
        Assert.Equal(20, fields["price"].GetProperty("precision").GetInt32());
        Assert.Equal(2, fields["price"].GetProperty("scale").GetInt32());
        Assert.Equal(JsonValueKind.Null, fields["price"].GetProperty("maxLength").ValueKind);
        Assert.Equal(["open", "closed"], fields["status"].GetProperty("values").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("item.department", fields["department"].GetProperty("labelKey").GetString());
        Assert.Equal(
            """{"entity":"Department","displayField":"name","recordsPath":"/api/apps/RecordsApp/entities/Department/records"}""",
            fields["department"].GetProperty("target").GetRawText());

        // A computed field is marked, so the form shows it read-only and never sends it.
        Assert.Equal(
            """{"name":"total","type":"decimal","labelKey":null,"required":false,"unique":false,"computed":true,"sequence":false,"maxLength":null,"precision":20,"scale":2,"values":null,"target":null,"fields":null}""",
            fields["total"].GetRawText());
        Assert.All(fieldList.Where(field => field.GetProperty("name").GetString() != "total"), field =>
            Assert.False(field.GetProperty("computed").GetBoolean()));

        // A child collection carries its child entity's fields, and no target: a child has no record API.
        Assert.Equal(
            """{"name":"parts","type":"child-collection","labelKey":null,"required":false,"unique":false,"computed":false,"sequence":false,"maxLength":null,"precision":null,"scale":null,"values":null,"target":null,"fields":[{"name":"name","type":"text","labelKey":null,"required":false,"unique":true,"computed":false,"sequence":false,"maxLength":100,"precision":null,"scale":null,"values":null,"target":null,"fields":null},{"name":"code","type":"text","labelKey":null,"required":false,"unique":false,"computed":true,"sequence":false,"maxLength":100,"precision":null,"scale":null,"values":null,"target":null,"fields":null}]}""",
            fields["parts"].GetRawText());
        Assert.All(fieldList.Where(field => field.GetProperty("name").GetString() != "parts"), field =>
            Assert.Equal(JsonValueKind.Null, field.GetProperty("fields").ValueKind));
    }

    [Fact]
    public async Task Page_with_a_table_over_a_data_source_returns_its_schema_and_no_entity()
    {
        using var page = await GetJsonAsync("/api/sites/records/pages/itemsbydepartment", HostA);

        var root = page.RootElement;
        Assert.Equal("ItemsByDepartment", root.GetProperty("name").GetString());
        var widget = Assert.Single(root.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("table", widget.GetProperty("type").GetString());
        Assert.Equal("ItemForm", widget.GetProperty("formPage").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("entity").ValueKind);

        var dataSource = widget.GetProperty("dataSource");
        Assert.Equal("ItemsByDepartment", dataSource.GetProperty("name").GetString());
        Assert.Equal("/api/apps/RecordsApp/data-sources/ItemsByDepartment/rows", dataSource.GetProperty("rowsPath").GetString());
        Assert.Equal("Item", dataSource.GetProperty("entity").GetString());
        Assert.Equal(10, dataSource.GetProperty("pageSize").GetInt32());

        var parameter = Assert.Single(dataSource.GetProperty("parameters").EnumerateArray().ToList());
        Assert.Equal(
            """{"name":"nameFilter","type":"text","required":false,"labelKey":null,"values":null,"target":null}""",
            parameter.GetRawText());

        // Each column has the type and label of the field its path ends at.
        var columns = dataSource.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["name", "departmentName", "department"], columns.Select(column => column.GetProperty("name").GetString()));
        Assert.Equal(
            """{"name":"departmentName","type":"text","labelKey":null,"values":null,"target":null}""",
            columns[1].GetRawText());
        Assert.Equal(
            """{"name":"department","type":"reference","labelKey":"item.department","values":null,"target":{"entity":"Department","displayField":"name","recordsPath":"/api/apps/RecordsApp/entities/Department/records"}}""",
            columns[2].GetRawText());
    }

    [Fact]
    public async Task Form_page_has_a_form_widget_without_a_form_page()
    {
        using var page = await GetJsonAsync("/api/sites/records/pages/itemform", HostA);

        Assert.Equal("ItemForm", page.RootElement.GetProperty("name").GetString());
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("form", widget.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("formPage").ValueKind);
        Assert.Equal("Item", widget.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("form").ValueKind);
    }

    [Fact]
    public async Task Form_page_over_a_form_returns_its_sections_with_read_only_flags_and_the_whole_entity()
    {
        using var page = await GetJsonAsync("/api/sites/records/pages/itemsections", HostA);

        Assert.Equal("ItemSections", page.RootElement.GetProperty("name").GetString());
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(
            ["type", "formPage", "entity", "dataSource", "form"],
            widget.EnumerateObject().Select(property => property.Name));
        Assert.Equal("form", widget.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("formPage").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("dataSource").ValueKind);

        // The total is computed, so it is read-only though the form does not say so.
        Assert.Equal(
            """{"name":"ItemSections","sections":[{"titleKey":"itemSections.main","fields":[{"name":"name","readOnly":false},{"name":"quantity","readOnly":true}]},{"titleKey":"itemSections.pricing","fields":[{"name":"price","readOnly":false},{"name":"total","readOnly":true}]}]}""",
            widget.GetProperty("form").GetRawText());

        // The entity keeps every field, so the SPA knows the type of each field the form shows.
        var entity = widget.GetProperty("entity");
        Assert.Equal("Item", entity.GetProperty("name").GetString());
        Assert.Equal(10, entity.GetProperty("fields").GetArrayLength());
    }

    [Theory]
    [InlineData("/api/sites/unknown", HostA, "No site is active under this path.")]
    [InlineData("/api/sites/api", HostA, "No site is active under this path.")]
    [InlineData("/api/sites/unknown/texts/en", HostA, "No site is active under this path.")]
    [InlineData("/api/sites/unknown/pages/Items", HostA, "No site is active under this path.")]
    [InlineData("/api/sites/records/texts/fr", HostA, "No text resources exist for this locale.")]
    [InlineData("/api/sites/records/pages/Unknown", HostA, "The site's application has no page with this name.")]
    [InlineData("/api/sites/second", HostB, "No site is active under this path.")]
    [InlineData("/api/sites/second/pages/Tasks", HostB, "No site is active under this path.")]
    public async Task Unknown_site_page_or_locale_is_a_404_problem(string path, string host, string title)
    {
        using var request = Request(path, host);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string host)
    {
        using var request = Request(path, host);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
