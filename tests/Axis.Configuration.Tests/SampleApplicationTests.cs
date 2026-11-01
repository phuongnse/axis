using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class SampleApplicationTests
{
    [Fact]
    public void Purchase_request_sample_compiles_without_diagnostics()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["departments/Department", "department-budgets/BudgetDecision", "department-budgets/Budget", "Department", "LineItem", "PurchaseRequest", "Supplier"], result.Model.Entities.Select(entity => entity.Name));
        Assert.Equal(3, result.Model.Seeds.Count);
        Assert.Equal("purchasing", Assert.Single(result.Model.Sites).Path);
        Assert.Equal(["en", "vi"], result.Model.Texts.Select(texts => texts.Locale));
        Assert.Single(result.Model.Processes, process => process.Name == "PurchaseRequestApproval");
        Assert.Single(result.Model.Forms, form => form.Name == "PurchaseRequestReview");
        Assert.Equal(["department-manager", "employee", "finance-reviewer", "operator", "purchasing-system"], result.Model.Roles.Select(role => role.Name));
        Assert.Equal("SubmitPurchaseRequest", Assert.Single(result.Model.Endpoints).Name);

        Assert.True(result.Model.TryGetEntity("PurchaseRequest", out var purchaseRequest));
        var approved = Assert.Single(result.Model.Events);
        Assert.Equal(("PurchaseRequestApproved", new EntityReference(purchaseRequest.Id, "PurchaseRequest")), (approved.Name, approved.Entity));
        Assert.Equal([new EventFieldModel("number", FieldType.Text), new EventFieldModel("totalAmount", FieldType.Decimal)], approved.Fields);
        Assert.True(purchaseRequest.TryGetField("number", out var number));
        Assert.NotNull(number.Sequence);
        Assert.Equal("PurchaseRequestNumber", number.Sequence.Name);
        Assert.Equal("PR-{yyyy}-{n:5}", number.Sequence.Format);
    }

    // A field named outer is reserved, so every sample must still compile.
    [Theory]
    [InlineData("purchase-requests")]
    [InlineData("leave-requests")]
    [InlineData("service-desk")]
    [InlineData("asset-management")]
    public void Every_sample_application_compiles_without_diagnostics(string sample)
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", sample), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void The_request_approval_template_compiles_without_diagnostics()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "SampleTemplates", "request-approval");
        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new ResourceContent(Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText(file)))
            .Where(file => file.Path != "template.json")
            .ToList();

        var result = ApplicationCompiler.CompileFiles(files, null, null);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Purchase_request_sample_held_in_memory_compiles_to_the_content_hash_of_its_folder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new ResourceContent(Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText(file)))
            .ToList();

        var fromFolder = ApplicationCompiler.Compile(folder, SamplePackages.Versions, SampleModules.Modules);
        var fromFiles = ApplicationCompiler.CompileFiles(files, SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(fromFiles.Diagnostics);
        Assert.NotNull(fromFolder.ContentHash);
        Assert.Equal(fromFolder.ContentHash, fromFiles.ContentHash);
    }

    [Fact]
    public void E2e_fixture_application_compiles_without_diagnostics_and_declares_the_sample_roles()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "e2e-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["department-manager", "employee", "finance-reviewer"], result.Model.Roles.Select(role => role.Name));
    }

    [Fact]
    public void Subprocess_fixture_application_compiles_without_diagnostics_with_a_parent_that_starts_a_child()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "subprocess-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["ChildJob", "ParentJob"], result.Model.Processes.Select(process => process.Name).Order());
    }

    [Fact]
    public void Host_site_fixture_application_compiles_without_diagnostics_with_one_site()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "host-site-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal("host-site", Assert.Single(result.Model.Sites).Path);
    }

    [Fact]
    public void Purchase_request_start_condition_message_has_an_English_and_a_Vietnamese_text()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions, SampleModules.Modules);

        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes, process => process.Name == "PurchaseRequestApproval");
        Assert.NotNull(process.StartCondition);
        Assert.Equal("purchaseRequest.cannotSubmit", process.StartCondition.Message.TextKey);
        foreach (var locale in new[] { "en", "vi" })
        {
            var texts = Assert.Single(result.Model.Texts, texts => texts.Locale == locale);
            Assert.True(texts.Texts.TryGetValue("purchaseRequest.cannotSubmit", out var text), $"The {locale} texts have no purchaseRequest.cannotSubmit.");
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void Leave_request_sample_maps_the_manager_slot_of_the_departments_module_to_department_manager()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "leave-requests"), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var module = Assert.Single(result.Model.Modules);
        Assert.Equal("departments", module.Id);
        Assert.Equal("1.1.0", module.Version);
        var policy = Assert.Single(result.Model.Policies, policy => policy.Name == "departments/ManagerAccess");
        Assert.Equal(["department-manager"], policy.Roles);
        var entityGrant = Assert.Single(policy.Grants, grant => grant.Entity is not null);
        Assert.Equal("departments/Department", entityGrant.Entity!.Name);
        Assert.NotNull(entityGrant.Filter);
        var dataSourceGrant = Assert.Single(policy.Grants, grant => grant.DataSource is not null);
        Assert.Equal("departments/Departments", dataSourceGrant.DataSource!.Name);
    }

    [Fact]
    public void Purchase_request_sample_maps_the_slots_of_the_departments_and_department_budgets_modules_to_department_manager()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions, SampleModules.Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["department-budgets 1.1.0", "departments 1.1.0"], result.Model.Modules.Select(module => $"{module.Id} {module.Version}").Order());
        var policy = Assert.Single(result.Model.Policies, policy => policy.Name == "department-budgets/ApproverAccess");
        Assert.Equal(["department-manager"], policy.Roles);
        var process = Assert.Single(result.Model.Processes, process => process.Name == "department-budgets/BudgetApproval");
        var review = Assert.IsType<TaskStepModel>(Assert.Single(process.Steps, step => step.Name == "review"));
        Assert.Equal("department-manager", review.Assignee.Role);

        var budget = Assert.Single(result.Model.Entities, entity => entity.Name == "department-budgets/Budget");
        var contract = Assert.IsType<ContractModel>(budget.Contract);
        Assert.Contains("department-manager", contract.Ceiling!.Roles.Keys);
        Assert.Equal(["approve", "reject"], contract.Lifecycle!.Transitions.Select(transition => transition.Name));
        Assert.All(contract.Lifecycle.Transitions, transition => Assert.NotNull(transition.Guard));
        Assert.Equal("amountFrozenOnceDecided", Assert.Single(contract.Validations).Name);
        var effect = Assert.Single(contract.Effects);
        Assert.Equal(("decisionRecorded", "approve", "department-budgets/BudgetDecision"), (effect.Name, effect.On, effect.Entity.Name));
    }
}
