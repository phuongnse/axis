using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class ApplicationLoaderTests
{
    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    [Fact]
    public void Valid_folder_loads_into_typed_resources_without_diagnostics()
    {
        var result = ApplicationLoader.Load(Fixture("valid-app"));

        Assert.Empty(result.Diagnostics);
        Assert.False(result.HasErrors);

        Assert.NotNull(result.Application);
        Assert.Equal(Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01"), result.Application.Id);
        Assert.Equal(ResourceKinds.Application, result.Application.Kind);
        Assert.Equal("PurchaseRequests", result.Application.Name);
        Assert.Equal(1, result.Application.FormatVersion);
        Assert.Equal(new TextReference("purchaseRequests.label"), result.Application.Label);

        Assert.Equal(["Department", "PurchaseRequest", "Supplier"], result.Entities.Select(entity => entity.Name));

        var purchaseRequest = Assert.Single(result.Entities, entity => entity.Name == "PurchaseRequest");
        Assert.Equal(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"), purchaseRequest.Id);
        Assert.Equal(ResourceKinds.Entity, purchaseRequest.Kind);
        Assert.Equal(new TextReference("purchaseRequest.label"), purchaseRequest.Label);
        Assert.Equal(
            ["title", "department", "supplier", "total", "neededBy", "status"],
            purchaseRequest.Fields.Select(field => field.Name));

        var title = purchaseRequest.Fields[0];
        Assert.Equal("text", title.Type);
        Assert.True(title.Required);
        Assert.Equal(200, title.MaxLength);

        var department = purchaseRequest.Fields[1];
        Assert.Equal("reference", department.Type);
        Assert.Equal("Department", department.Target);

        var total = purchaseRequest.Fields[3];
        Assert.Equal("decimal", total.Type);
        Assert.Equal(18, total.Precision);
        Assert.Equal(2, total.Scale);
        Assert.Null(total.Required);

        var status = purchaseRequest.Fields[5];
        Assert.Equal("enum", status.Type);
        Assert.Equal(["draft", "submitted", "approved", "rejected"], status.Values);
        Assert.Equal(new TextReference("purchaseRequest.status.label"), status.Label);
    }

    [Fact]
    public void Folder_with_every_failure_reports_all_diagnostics_in_one_pass_sorted_by_file_then_path()
    {
        var result = ApplicationLoader.Load(Fixture("broken-app"));

        Assert.True(result.HasErrors);
        Assert.Equal(
            [
                (DiagnosticCodes.DuplicateName, "entities/department.json", "/name"),
                (DiagnosticCodes.InvalidJson, "entities/malformed.json", ""),
                (DiagnosticCodes.MissingKind, "entities/missing-kind.json", ""),
                (DiagnosticCodes.SchemaViolation, "entities/schema-violation.json", "/colour"),
                (DiagnosticCodes.SchemaViolation, "entities/schema-violation.json", "/fields/0/type"),
                (DiagnosticCodes.DuplicateName, "entities/schema-violation.json", "/name"),
                (DiagnosticCodes.DuplicateId, "entities/supplier.json", "/id"),
                (DiagnosticCodes.UnknownKind, "entities/unknown-kind.json", "/kind"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public void Malformed_json_does_not_stop_other_files_from_loading()
    {
        var result = ApplicationLoader.Load(Fixture("broken-app"));

        var invalidJson = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.InvalidJson);
        Assert.Equal("entities/malformed.json", invalidJson.File);
        Assert.Equal("", invalidJson.Path);
        Assert.Contains("line 5", invalidJson.Message, StringComparison.Ordinal);

        Assert.NotNull(result.Application);
        Assert.Equal(
            ["Department", "Department", "Invoice", "Supplier"],
            result.Entities.Select(entity => entity.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Schema_invalid_file_still_takes_part_in_duplicate_detection()
    {
        var result = ApplicationLoader.Load(Fixture("broken-app"));
        var violationId = Guid.Parse("11111111-1111-4111-8111-111111111104");

        var duplicateId = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.DuplicateId);
        Assert.Equal("entities/supplier.json", duplicateId.File);
        Assert.Equal(violationId, duplicateId.ResourceId);
        Assert.Contains("entities/schema-violation.json", duplicateId.Message, StringComparison.Ordinal);

        var duplicateName = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCodes.DuplicateName && diagnostic.File == "entities/schema-violation.json");
        Assert.Contains("entities/invoice.json", duplicateName.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Entities, entity => entity.Id == violationId && entity.Name == "Invoice");
    }

    [Fact]
    public void Folder_without_a_manifest_reports_it_missing()
    {
        var result = ApplicationLoader.Load(Fixture("no-manifest"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ManifestMissing, "application.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Application);
        Assert.Single(result.Entities);
    }

    [Fact]
    public void Folder_with_two_manifests_reports_the_extra_one()
    {
        var result = ApplicationLoader.Load(Fixture("two-manifests"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MultipleManifests, "extra/application.json", "/kind"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("PurchaseRequests", result.Application?.Name);
    }

    [Fact]
    public void Kind_that_is_not_a_string_is_reported_at_the_kind_property()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("thing.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": 7 }""");

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.MissingKind, "thing.json", "/kind"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Missing_required_property_is_reported_at_the_resource_root()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1 }""");

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.SchemaViolation, "order.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("fields", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_property_in_one_file_is_invalid_json()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "A", "name": "B", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""");

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.InvalidJson, "order.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'name'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_manifest_is_not_also_reported_as_missing()
    {
        using var folder = new TemporaryFolder().With("application.json", "{ not json");

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.InvalidJson, "application.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Names_are_compared_without_letter_case_and_ids_as_uuids()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("a.json", """{ "id": "AAAAAAAA-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""")
            .With("b.json", """{ "id": "aaaaaaaa-1111-4111-8111-111111111111", "kind": "entity", "name": "order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""");

        var result = ApplicationLoader.Load(folder.Path);

        Assert.Equal(
            [(DiagnosticCodes.DuplicateId, "b.json", "/id"), (DiagnosticCodes.DuplicateName, "b.json", "/name")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
    }

    [Fact]
    public void Same_name_in_different_kinds_is_allowed()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("sample.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Sample", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""");

        Assert.Empty(ApplicationLoader.Load(folder.Path).Diagnostics);
    }

    [Fact]
    public void Integral_numbers_written_with_a_decimal_point_load_as_integers()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1.0, "fields": [{ "name": "number", "type": "text", "maxLength": 20.0 }] }""");

        var result = ApplicationLoader.Load(folder.Path);

        Assert.Empty(result.Diagnostics);
        var order = Assert.Single(result.Entities);
        Assert.Equal(1, order.FormatVersion);
        Assert.Equal(20, order.Fields[0].MaxLength);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
