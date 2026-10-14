using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class PackageStepCompilerTests
{
    private const string OrderId = "11111111-1111-4111-8111-111111111111";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";

    private const string Packages = """[{ "id": "test-package", "version": "^1.0.0" }]""";

    private const string SumInput = """ "input": { "a": "1", "b": "2" }, """;

    [Fact]
    public void A_valid_package_step_compiles_to_a_step_model_that_holds_the_pinned_version()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/SUM",
                  "input": { "B": "amount", "a": "1" }, "output": { "total": "AMOUNT" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(("add", "test-package", "1.0.0", "sum", "done"), (step.Name, step.Package, step.Version, step.Operation, step.Next));
        Assert.Equal([("a", "1"), ("b", "amount")], step.Input.Select(input => (input.Input, input.Value.Expression)));
        Assert.Equal([new ConnectorOutputModel("total", "amount")], step.Output);
        Assert.Equal(new RetryPolicyModel(3, TimeSpan.FromSeconds(30)), step.Retry);
        Assert.False(step.Idempotent);
        Assert.Equal(TimeSpan.FromSeconds(5), step.Timeout);
    }

    [Fact]
    public void A_package_step_keeps_its_retry_policy_and_the_manifest_idempotency()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/describe",
                  "input": { "name": "status" }, "retry": { "attempts": 10, "backoff": "PT1M" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Empty(result.Diagnostics);
        var step = Assert.IsType<PackageOperationStepModel>(Assert.Single(result.Model!.Processes).Steps[0]);
        Assert.Equal(new RetryPolicyModel(10, TimeSpan.FromMinutes(1)), step.Retry);
        Assert.True(step.Idempotent);
        Assert.Equal(TimeSpan.FromSeconds(30), step.Timeout);
    }

    [Fact]
    public void An_operation_of_a_package_the_application_does_not_list_is_reported_at_the_operation()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "other-package/sum", "input": {}, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownStepPackage, "processes/order-review.json", "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            "The package 'other-package' is not one of the application's packages. Add it to 'packages' in application.json.",
            diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_operation_of_a_package_when_the_application_lists_none_is_reported()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(Process(
            """
            { "name": "add", "type": "operation", "operation": "test-package/sum", "input": {}, "next": "done" }
            """));

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownStepPackage, "/steps/0/operation"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_operation_of_a_listed_package_that_failed_pinning_is_not_reported_again()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                $$"""
                { "name": "add", "type": "operation", "operation": "test-package/sum", {{SumInput}} "next": "done" }
                """),
            packages: """[{ "id": "test-package", "version": "^2.0.0" }]""");

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.NoPackageVersionInRange, diagnostic.Code);
    }

    [Fact]
    public void An_unknown_operation_of_a_pinned_package_is_reported_at_the_operation()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/multiply", "input": {}, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownPackageOperation, "processes/order-review.json", "/steps/0/operation"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(
            "The operation 'multiply' was not found. Package 'test-package' 1.0.0 has no operation with that name.",
            diagnostic.Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_input_expression_of_the_wrong_type_is_reported_at_its_key()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/sum",
                  "input": { "a": "'many'", "b": "2" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/input/a", "The expression must be integer, but it is text."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_missing_required_input_is_reported_at_the_input()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/sum", "input": { "a": "1" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingConnectorInput, "/steps/0/input", "The input 'b' is required by the operation 'sum', but the step does not give it."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void An_output_that_does_not_fit_its_subject_field_is_reported_at_its_key()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                $$"""
                { "name": "add", "type": "operation", "operation": "test-package/sum",
                  {{SumInput}} "output": { "total": "status" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidOutputField, "/steps/0/output/total", "The output 'total' is integer, so it cannot be written to the field 'status', which is text."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void An_invalid_retry_backoff_is_reported_for_a_package_step()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                $$"""
                { "name": "add", "type": "operation", "operation": "test-package/sum",
                  {{SumInput}} "retry": { "backoff": "soon" }, "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidRetryBackoff, "/steps/0/retry/backoff"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData(""" "set": { "status": "'x'" }, """)]
    [InlineData(""" "connector": { "name": "Purchasing", "operation": "createOrder" }, """)]
    [InlineData(""" "event": "OrderPlaced", """)]
    [InlineData(""" "fields": { "status": "'x'" }, """)]
    public void A_package_step_with_a_property_of_another_operation_is_a_schema_error(string property)
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                $$"""
                { "name": "add", "type": "operation", "operation": "test-package/sum", {{SumInput}} {{property}} "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_package_step_without_input_is_a_schema_error()
    {
        using var packages = new TemporaryFolder().Install("1.0.0");
        using var folder = Folder(
            Process(
                """
                { "name": "add", "type": "operation", "operation": "test-package/sum", "next": "done" }
                """),
            packages: Packages);

        var result = ApplicationCompiler.Compile(folder.Path, PackageCatalog.Load(packages.Path).Versions);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_operation_that_is_not_a_name_or_a_package_operation_is_a_schema_error()
    {
        using var folder = Folder(Process(
            """
            { "name": "add", "type": "operation", "operation": "Test-Package/sum/extra", "input": {}, "next": "done" }
            """));

        var result = ApplicationCompiler.Compile(folder.Path, []);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
    }

    private static string Process(string step) =>
        $$"""
        { "id": "{{ProcessId}}", "kind": "process", "name": "OrderReview", "formatVersion": 1,
          "entity": "Order", "start": "add",
          "steps": [ {{step}}, { "name": "done", "type": "end" } ] }
        """;

    /// <summary>An application with an <c>Order</c> entity, the given process and the given <c>packages</c> of the manifest.</summary>
    private static TemporaryFolder Folder(string process, string? packages = null) =>
        new TemporaryFolder()
            .With("application.json", $$"""
                { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1
                  {{(packages is null ? "" : $", \"packages\": {packages}")}} }
                """)
            .With("entities/order.json", $$"""
                { "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
                  "fields": [
                    { "name": "amount", "type": "integer" },
                    { "name": "status", "type": "text" }
                  ] }
                """)
            .With("processes/order-review.json", process);
}
