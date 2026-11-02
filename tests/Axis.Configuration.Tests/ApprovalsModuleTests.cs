using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

/// <summary>
/// The built-in module <c>approvals</c> 1.0.0 under <c>modules/approvals</c>, which the test project copies to the
/// test output as <c>BuiltInModules/approvals/1.0.0</c>. It depends on the built-in module <c>organization</c>.
/// </summary>
public sealed class ApprovalsModuleTests
{
    private static readonly string _modulesPath = Path.Combine(AppContext.BaseDirectory, "BuiltInModules");

    [Fact]
    public void The_module_loads_through_the_catalog_with_the_slots_admin_and_member()
    {
        var catalog = ModuleCatalog.Load(_modulesPath);

        Assert.Empty(catalog.Diagnostics);
        var module = Assert.Single(catalog.Modules, module => module.Manifest.Id == "approvals");
        Assert.Equal(("approvals", "1.0.0"), (module.Manifest.Id, module.Manifest.Version));
        Assert.Equal(["admin", "member"], module.Manifest.Roles.Select(slot => slot.Name));
    }

    [Fact]
    public void An_application_that_declares_both_modules_and_maps_their_slots_compiles_without_diagnostics()
    {
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(["approvals", "organization"], compiled.Model!.Modules.Select(module => module.Id).Order());
        Assert.All(
            compiled.Model.Modules,
            module => Assert.Equal(
                new Dictionary<string, string> { ["admin"] = "admin", ["member"] = "employee" },
                module.Roles));
    }

    [Fact]
    public void The_requester_employee_is_matched_by_user_from_the_requester_and_the_steps_are_a_child_collection()
    {
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        var request = Entity(compiled.Model!, "approvals/Request");
        var requesterEmployee = Field(request, "requesterEmployee");
        Assert.Equal(FieldType.Reference, requesterEmployee.Type);
        Assert.EndsWith("Employee", requesterEmployee.Target!.Name, StringComparison.Ordinal);
        Assert.Equal(new FieldMatchModel("user", "requester"), requesterEmployee.Match);
        Assert.True(Field(request, "requester").Creator);
        Assert.Equal("approvals/RequestNumber", Field(request, "number").Sequence!.Name);

        var requestType = Entity(compiled.Model!, "approvals/RequestType");
        var steps = Field(requestType, "steps");
        Assert.Equal(FieldType.ChildCollection, steps.Type);
        Assert.EndsWith("ApprovalStep", steps.Target!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_that_grants_a_member_role_update_on_a_request_without_fields_is_past_the_ceiling()
    {
        using var folder = Application().With("policies/employee-requests.json", """
            { "id": "66666666-6666-4666-8666-666666666661", "kind": "policy", "name": "EmployeeRequests", "formatVersion": 1,
              "roles": ["employee"],
              "grants": [{ "name": "requestUpdate", "entity": "approvals/Request", "actions": ["update"] }] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.GrantPastCeiling);
    }

    private static EntityModel Entity(ApplicationModel model, string name) =>
        Assert.Single(model.Entities, entity => entity.Name == name);

    private static FieldModel Field(EntityModel entity, string name) =>
        Assert.Single(entity.Fields, field => field.Name == name);

    private static TemporaryFolder Application() => new TemporaryFolder()
        .With("application.json", """
            { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e03", "kind": "application", "name": "Sample", "formatVersion": 1,
              "modules": [
                { "id": "organization", "version": "^1.0.0", "roles": { "admin": "admin", "member": "employee" } },
                { "id": "approvals", "version": "^1.0.0", "roles": { "admin": "admin", "member": "employee" } }
              ] }
            """)
        .With("texts/en.json", """
            { "id": "55555555-5555-4555-8555-555555555557", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "role.admin": "Administrator", "role.employee": "Employee" } }
            """)
        .With("roles/admin.json", """
            { "id": "44444444-4444-4444-8444-444444444431", "kind": "role", "name": "admin", "formatVersion": 1, "label": { "textKey": "role.admin" } }
            """)
        .With("roles/employee.json", """
            { "id": "44444444-4444-4444-8444-444444444432", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
            """);
}
