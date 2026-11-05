using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Templates;

namespace Axis.Configuration.Tests;

/// <summary>
/// The built-in application <c>approvals</c> 1.0.0 under <c>applications/approvals</c>, which the test project copies
/// to the test output as <c>BuiltInApplications/approvals/1.0.0</c>. It declares the built-in modules
/// <c>organization</c> and <c>approvals</c> and holds only roles, a site, a sign-in and texts.
/// </summary>
public sealed class ApprovalsApplicationTests
{
    private static readonly string _applicationsPath = Path.Combine(AppContext.BaseDirectory, "BuiltInApplications");
    private static readonly string _modulesPath = Path.Combine(AppContext.BaseDirectory, "BuiltInModules");

    private static readonly string[] _sitePages =
    [
        "approvals/MyRequests",
        "approvals/ToDecide",
        "organization/OutOfOffice",
        "approvals/AllRequests",
        "approvals/RequestTypes",
        "organization/Departments",
        "organization/Employees",
    ];

    [Fact]
    public void The_template_loads_through_the_catalog_as_a_built_in_application()
    {
        var catalog = TemplateCatalog.Load(_applicationsPath);

        Assert.Empty(catalog.Problems);
        var template = Assert.Single(catalog.Newest);
        Assert.Equal(("approvals", "1.0.0", true), (template.Manifest.Id, template.Manifest.Version, template.Manifest.BuiltIn));
    }

    [Fact]
    public void The_template_compiles_with_both_modules_and_maps_their_slots_to_admin_and_employee()
    {
        var template = Template();

        var compiled = ApplicationCompiler.CompileFiles(template.Files, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(["approvals", "organization"], compiled.Model!.Modules.Select(module => module.Id).Order());
        Assert.All(
            compiled.Model.Modules,
            module => Assert.Equal(
                new Dictionary<string, string> { ["admin"] = "admin", ["member"] = "employee" },
                module.Roles));
    }

    [Fact]
    public void The_template_holds_no_entity_process_page_or_policy_of_its_own()
    {
        var template = Template();

        var compiled = ApplicationCompiler.CompileFiles(template.Files, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        var model = compiled.Model!;
        Assert.All(
            model.Entities.Select(entity => entity.Name)
                .Concat(model.Processes.Select(process => process.Name))
                .Concat(model.Pages.Select(page => page.Name))
                .Concat(model.Policies.Select(policy => policy.Name)),
            name => Assert.True(
                name.StartsWith("organization/", StringComparison.Ordinal) || name.StartsWith("approvals/", StringComparison.Ordinal),
                $"'{name}' is not a resource of the organization or approvals module."));

        string[] kinds = ["entity", "process", "page", "policy"];
        Assert.All(template.Files, file =>
        {
            using var document = JsonDocument.Parse(file.Content);
            var kind = document.RootElement.GetProperty("kind").GetString();
            Assert.DoesNotContain(kind, kinds);
        });
    }

    [Fact]
    public void The_site_links_the_seven_module_pages_in_order()
    {
        var template = Template();

        var compiled = ApplicationCompiler.CompileFiles(template.Files, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        var site = Assert.Single(compiled.Model!.Sites);
        Assert.Equal(_sitePages, site.Navigation.Select(entry => entry.Page.Name));
    }

    [Fact]
    public void Every_text_key_of_the_template_is_in_en_and_vi()
    {
        var template = Template();
        var en = Texts(template, "en");
        var vi = Texts(template, "vi");

        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), vi.Keys.Order(StringComparer.Ordinal));
        Assert.All(en.Concat(vi), text => Assert.False(string.IsNullOrWhiteSpace(text.Value), $"The text '{text.Key}' is blank."));

        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in template.Files.Where(file => file.Path is not ("texts/en.json" or "texts/vi.json")))
        {
            using var document = JsonDocument.Parse(file.Content);
            CollectTextKeys(document.RootElement, used);
        }

        Assert.NotEmpty(used);
        Assert.All(used, key =>
        {
            Assert.True(en.ContainsKey(key), $"The text key '{key}' is missing in en.");
            Assert.True(vi.ContainsKey(key), $"The text key '{key}' is missing in vi.");
        });
    }

    private static InstalledTemplate Template()
    {
        var catalog = TemplateCatalog.Load(_applicationsPath);
        Assert.Empty(catalog.Problems);
        return Assert.Single(catalog.Newest);
    }

    private static Dictionary<string, string> Texts(InstalledTemplate template, string locale)
    {
        var file = Assert.Single(template.Files, file => file.Path == $"texts/{locale}.json");
        using var document = JsonDocument.Parse(file.Content);
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(text => text.Name, text => text.Value.GetString()!);
    }

    private static void CollectTextKeys(JsonElement element, ISet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "textKey" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        keys.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectTextKeys(property.Value, keys);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectTextKeys(item, keys);
                }

                break;
        }
    }
}
