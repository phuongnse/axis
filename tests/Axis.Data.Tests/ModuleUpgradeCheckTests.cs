using Axis.Configuration.Diagnostics;
using Axis.Configuration.Storage;

namespace Axis.Data.Tests;

public sealed class ModuleUpgradeCheckTests
{
    private static readonly Guid _departmentId = Guid.Parse("5c1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c07");

    private static readonly Guid _otherId = Guid.Parse("0b7a2c1e-3d4f-4a5b-8c6d-7e8f9a0b1c2d");

    private const string Code = """{"name":"code","type":"text"}""";
    private const string Name = """{"name":"name","type":"text"}""";
    private const string Budget = """{"name":"budget","type":"decimal"}""";

    [Fact]
    public void Identical_copies_give_no_diagnostic()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code, Name));
        var next = Module("1.0.0", Entity("Department.json", _departmentId, Code, Name));

        Assert.Empty(ModuleUpgradeCheck.Check(active, next));
    }

    [Fact]
    public void A_new_field_a_new_entity_and_a_new_module_give_no_diagnostic()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code));
        var next = Module("1.1.0", Entity("Department.json", _departmentId, Code, Budget), Entity("Office.json", _otherId, Name))
            .Concat(Module("1.0.0", id: "other", Entity("Thing.json", Guid.NewGuid(), Name)))
            .ToList();

        Assert.Empty(ModuleUpgradeCheck.Check(active, next));
    }

    [Fact]
    public void A_new_process_file_gives_no_diagnostic()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code));
        var next = Module("1.1.0", Entity("Department.json", _departmentId, Code));
        next.Add(Resource(
            "modules/departments/processes/Review.json",
            $$"""{"id":"{{Guid.NewGuid()}}","kind":"process","name":"Review","entity":"Department","start":"done","steps":[{"name":"done","type":"end"}]}"""));

        Assert.Empty(ModuleUpgradeCheck.Check(active, next));
    }

    [Fact]
    public void Nothing_to_compare_gives_no_diagnostic()
    {
        var next = Module("1.0.0", Entity("Department.json", _departmentId, Code));

        Assert.Empty(ModuleUpgradeCheck.Check([], next));
        Assert.Empty(ModuleUpgradeCheck.Check([Plain("Request.json")], next));
    }

    [Fact]
    public void A_changed_field_type_is_reported_at_the_field_type_in_the_new_file()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code, Name));
        var next = Module("1.1.0", Entity("Department.json", _departmentId, Code, """{"name":"name","type":"integer"}"""));

        var diagnostic = Assert.Single(ModuleUpgradeCheck.Check(active, next));

        Assert.Equal(
            (DiagnosticCodes.NonAdditiveModuleUpgrade, "modules/departments/Department.json", "/fields/1/type", _departmentId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'departments'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1.1.0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropped_field_is_reported_at_the_fields_of_the_new_file_and_names_the_field_ignoring_letter_case()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code, Name));
        var next = Module("1.1.0", Entity("Department.json", _departmentId, """{"name":"CODE","type":"text"}"""));

        var diagnostic = Assert.Single(ModuleUpgradeCheck.Check(active, next));

        Assert.Equal((DiagnosticCodes.NonAdditiveModuleUpgrade, "modules/departments/Department.json", "/fields"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'name'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropped_entity_is_reported_at_the_file_of_the_active_release_and_names_both_versions()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code), Entity("Office.json", _otherId, Name));
        var next = Module("1.1.0", Entity("Office.json", _otherId, Name));

        var diagnostic = Assert.Single(ModuleUpgradeCheck.Check(active, next));

        Assert.Equal(
            (DiagnosticCodes.NonAdditiveModuleUpgrade, "modules/departments/Department.json", "", _departmentId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'departments'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1.1.0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_matches_by_its_id_so_a_new_file_name_is_not_a_drop()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code));
        var next = Module("1.1.0", Entity("Departments/Department.json", _departmentId, Code));

        Assert.Empty(ModuleUpgradeCheck.Check(active, next));
    }

    [Fact]
    public void A_removed_module_reports_each_of_its_entities()
    {
        var active = Module("1.0.0", Entity("Department.json", _departmentId, Code), Entity("Office.json", _otherId, Name));

        var diagnostics = ModuleUpgradeCheck.Check(active, [Plain("Request.json")]);

        Assert.Equal(
            ["modules/departments/Department.json", "modules/departments/Office.json"],
            diagnostics.Select(diagnostic => diagnostic.File));
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.NonAdditiveModuleUpgrade, diagnostic.Code));
        Assert.Contains("none", diagnostics[0].Message, StringComparison.Ordinal);
    }

    private static List<ReleaseResource> Module(string version, params ReleaseResource[] entities) => Module(version, "departments", entities);

    private static List<ReleaseResource> Module(string version, string id, params ReleaseResource[] entities) =>
        [
            Resource($"modules/{id}/module.json", $$"""{"kind":"module","id":"{{id}}","version":"{{version}}","label":"Module"}"""),
            Plain("Request.json"),
            .. entities.Select(entity => Resource(entity.Path.Replace("modules/departments/", $"modules/{id}/", StringComparison.Ordinal), entity.Content)),
        ];

    private static ReleaseResource Entity(string file, Guid id, params string[] fields) =>
        Resource($"modules/departments/{file}", $$"""{"id":"{{id}}","kind":"entity","name":"{{Path.GetFileNameWithoutExtension(file)}}","fields":[{{string.Join(",", fields)}}]}""");

    private static ReleaseResource Plain(string path) =>
        Resource(path, $$"""{"id":"{{Guid.NewGuid()}}","kind":"entity","name":"Request","fields":[]}""");

    private static ReleaseResource Resource(string path, string content) =>
        new() { ReleaseId = Guid.Empty, Path = path, Content = content };
}
