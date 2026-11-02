using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

/// <summary>
/// The built-in module <c>organization</c> 1.0.0 under <c>modules/organization</c>, which the test project copies to
/// the test output as <c>BuiltInModules/organization/1.0.0</c>.
/// </summary>
public sealed class OrganizationModuleTests
{
    private static readonly string _modulesPath = Path.Combine(AppContext.BaseDirectory, "BuiltInModules");

    [Fact]
    public void The_module_loads_through_the_catalog_with_the_slots_admin_and_member()
    {
        var catalog = ModuleCatalog.Load(_modulesPath);

        Assert.Empty(catalog.Diagnostics);
        var module = Assert.Single(catalog.Modules, module => module.Manifest.Id == "organization");
        Assert.Equal(("organization", "1.0.0"), (module.Manifest.Id, module.Manifest.Version));
        Assert.Equal(["admin", "member"], module.Manifest.Roles.Select(slot => slot.Name));
    }

    [Fact]
    public void An_application_that_declares_the_module_and_maps_both_slots_compiles_without_diagnostics()
    {
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(
            new Dictionary<string, string> { ["admin"] = "admin", ["member"] = "employee" },
            Assert.Single(compiled.Model!.Modules).Roles);
    }

    [Fact]
    public void The_phone_is_sensitive_and_the_work_email_and_the_account_are_unique()
    {
        using var folder = Application();

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        Assert.Empty(compiled.Diagnostics);
        var employee = Assert.Single(compiled.Model!.Entities, entity => entity.Name.EndsWith("Employee", StringComparison.Ordinal));
        Assert.True(Field(employee, "phone").Sensitive);
        Assert.True(Field(employee, "workEmail").Unique);
        Assert.True(Field(employee, "user").Unique);
        Assert.DoesNotContain(employee.Fields, field => field.Name is "birthDate" or "dateOfBirth" or "identityNumber" or "address");
        var department = Assert.Single(compiled.Model.Entities, entity => entity.Name.EndsWith("Department", StringComparison.Ordinal));
        Assert.True(Field(department, "code").Unique);
    }

    [Fact]
    public void A_policy_that_grants_the_member_role_a_wider_update_than_the_ceiling_is_axc0334()
    {
        using var folder = Application().With("policies/employee-update.json", """
            { "id": "44444444-4444-4444-8444-444444444421", "kind": "policy", "name": "EmployeeUpdate", "formatVersion": 1,
              "roles": ["employee"],
              "grants": [{ "name": "employeeUpdate", "entity": "organization/Employee", "actions": ["update"] }] }
            """);

        var compiled = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(_modulesPath).Modules);

        var diagnostic = Assert.Single(compiled.Diagnostics);
        Assert.Equal(DiagnosticCodes.GrantPastCeiling, diagnostic.Code);
        Assert.Contains("'employee'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'update'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'member'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(compiled.Model);
    }

    [Theory]
    [InlineData("2026-11-02", "2026-11-05", true)]
    [InlineData("2026-10-30", "2026-11-02", true)]
    [InlineData("2026-10-30", "2026-11-05", true)]
    [InlineData("2026-11-03", "2026-11-05", false)]
    [InlineData("2026-10-28", "2026-11-01", false)]
    [InlineData(null, "2026-11-05", false)]
    [InlineData("2026-10-30", null, false)]
    [InlineData(null, null, false)]
    public void Is_delegating_is_true_only_when_today_is_between_the_two_dates_both_included(string? from, string? until, bool expected)
    {
        var rule = LoadRule();
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var fields = new Dictionary<string, ExpressionType> { ["f"] = ExpressionType.Date, ["u"] = ExpressionType.Date };
        var values = new ExpressionValues(new Dictionary<string, object?> { ["f"] = Date(from), ["u"] = Date(until) });
        var parsed = ExpressionParser.Parse("IsDelegating(f, u)");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope(fields, [rule]), ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var result = ExpressionInterpreter.Evaluate(parsed.Expression, check, values, now: now, timeZone: TimeZoneInfo.Utc);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Is_delegating_follows_the_time_zone_of_the_application_near_midnight()
    {
        var rule = LoadRule();
        var now = new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
        var saigon = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var parsed = ExpressionParser.Parse("IsDelegating(date('2026-11-03'), date('2026-11-05'))");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope([], [rule]), ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var utc = ExpressionInterpreter.Evaluate(parsed.Expression, check, new ExpressionValues([]), now: now, timeZone: TimeZoneInfo.Utc);
        var inSaigon = ExpressionInterpreter.Evaluate(parsed.Expression, check, new ExpressionValues([]), now: now, timeZone: saigon);

        Assert.Equal(false, utc.Value);
        Assert.Equal(true, inSaigon.Value);
    }

    private static DateOnly? Date(string? text) =>
        text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static Axis.Configuration.Model.FieldModel Field(Axis.Configuration.Model.EntityModel entity, string name) =>
        Assert.Single(entity.Fields, field => field.Name == name);

    /// <summary>The rule built from the manifest file of the module, as the compiler builds it.</summary>
    private static ExpressionRule LoadRule()
    {
        var file = Path.Combine(_modulesPath, "organization", "1.0.0", "rules", "is-delegating.json");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal("boolean", json["resultType"]!.GetValue<string>());
        var parameters = json["parameters"]!.AsArray()
            .Select(parameter => new ExpressionRuleParameter(parameter!["name"]!.GetValue<string>(), ExpressionType.Date))
            .ToList();
        Assert.All(json["parameters"]!.AsArray(), parameter => Assert.Equal("date", parameter!["type"]!.GetValue<string>()));
        Assert.Equal(["from", "until"], parameters.Select(parameter => parameter.Name));

        var expression = json["expression"]!.GetValue<string>();
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var scope = new ExpressionScope(parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Type));
        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return new ExpressionRule(json["name"]!.GetValue<string>(), parameters, ExpressionType.Boolean)
        {
            Expression = expression,
            Body = parsed.Expression,
            BodyCheck = check,
        };
    }

    private static TemporaryFolder Application() => new TemporaryFolder()
        .With("application.json", """
            { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e02", "kind": "application", "name": "Sample", "formatVersion": 1,
              "modules": [{ "id": "organization", "version": "^1.0.0", "roles": { "admin": "admin", "member": "employee" } }] }
            """)
        .With("texts/en.json", """
            { "id": "55555555-5555-4555-8555-555555555556", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
              "texts": { "role.admin": "Administrator", "role.employee": "Employee" } }
            """)
        .With("roles/admin.json", """
            { "id": "44444444-4444-4444-8444-444444444411", "kind": "role", "name": "admin", "formatVersion": 1, "label": { "textKey": "role.admin" } }
            """)
        .With("roles/employee.json", """
            { "id": "44444444-4444-4444-8444-444444444412", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }
            """);
}
