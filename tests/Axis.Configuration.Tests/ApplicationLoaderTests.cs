using System.Runtime.Versioning;
using System.Text.Json;
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
        Assert.Equal("application.json", result.Application.File);

        Assert.Equal(["Department", "PurchaseRequest", "Supplier"], result.Entities.Select(entity => entity.Name));

        var purchaseRequest = Assert.Single(result.Entities, entity => entity.Name == "PurchaseRequest");
        Assert.Equal(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"), purchaseRequest.Id);
        Assert.Equal(ResourceKinds.Entity, purchaseRequest.Kind);
        Assert.Equal(new TextReference("purchaseRequest.label"), purchaseRequest.Label);
        Assert.Equal("entities/purchase-request.json", purchaseRequest.File);
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

        Assert.Equal("name", Assert.Single(result.Entities, entity => entity.Name == "Department").DisplayField);
        Assert.Null(purchaseRequest.DisplayField);

        Assert.Equal(2, result.Texts.Count);
        var english = result.Texts[0];
        Assert.Equal(
            (ResourceKinds.Text, "TextsEn", "en", "texts/en.json"),
            (english.Kind, english.Name, english.Locale, english.File));
        Assert.Equal("Purchase requests", english.Texts["purchaseRequests.label"]);
        Assert.Equal(9, english.Texts.Count);

        var site = Assert.Single(result.Sites);
        Assert.Equal(
            (ResourceKinds.Site, "Purchasing", "purchasing", "sites/purchasing.json"),
            (site.Kind, site.Name, site.Path, site.File));
        Assert.Equal(["PurchaseRequestForm", "PurchaseRequests"], result.Pages.Select(page => page.Name));
        Assert.Empty(result.UnloadedPageNames);
    }

    [Theory]
    [InlineData(""" "locale": "english" """, "/locale")]
    [InlineData(""" "locale": "en_US" """, "/locale")]
    [InlineData(""" "locale": "en", "texts": { "a.label": 1 } """, "/texts/a.label")]
    [InlineData(""" "locale": "en", "label": { "textKey": "a.label" } """, "/label")]
    public void Text_resource_with_a_bad_locale_text_or_property_is_a_schema_violation(string properties, string path)
    {
        var texts = properties.Contains("\"texts\"", StringComparison.Ordinal) ? properties : $$"""{{properties}}, "texts": {}""";
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("texts/en.json", $$"""{ "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "Texts", "formatVersion": 1, {{texts}} }""");

        var result = ApplicationLoader.Load(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "texts/en.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Empty(result.Texts);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    [InlineData("pt-BR")]
    [InlineData("zh-Hant-TW")]
    public void Text_resource_accepts_locale_tags(string locale)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("texts/texts.json", $$"""{ "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "Texts", "formatVersion": 1, "locale": "{{locale}}", "texts": { "a.label": "A" } }""");

        var result = ApplicationLoader.Load(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(locale, Assert.Single(result.Texts).Locale);
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
    public void Manifest_only_in_a_subfolder_is_reported_as_misplaced()
    {
        using var folder = new TemporaryFolder().With("config/application.json", Manifest);

        var result = ApplicationLoader.Load(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MisplacedManifest, "config/application.json", "/kind"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01"), diagnostic.ResourceId);
        Assert.Null(result.Application);
    }

    [Fact]
    public void Root_application_file_of_another_kind_is_reported_as_misplaced()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""");

        var result = ApplicationLoader.Load(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MisplacedManifest, "application.json", "/kind"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'entity'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Application);
        Assert.Equal("Order", Assert.Single(result.Entities).Name);
    }

    [Fact]
    public void Unreadable_file_is_reported_without_stopping_the_load()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("locked.json", """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Locked", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""")
            .With("order.json", """{ "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }""");

        // An exclusive lock makes the loader's read fail with an IOException on every platform.
        using (File.Open(Path.Combine(folder.Path, "locked.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = ApplicationLoader.Load(folder.Path);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(
                (DiagnosticCodes.UnreadableFile, "locked.json", ""),
                (diagnostic.Code, diagnostic.File, diagnostic.Path));
            Assert.Equal("The file could not be read.", diagnostic.Message);
            Assert.DoesNotContain(folder.Path, diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal("Sample", result.Application?.Name);
            Assert.Equal("Order", Assert.Single(result.Entities).Name);
        }
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
        Assert.Equal("The file is not valid JSON: property 'name' appears more than once.", diagnostic.Message);
        Assert.DoesNotContain(folder.Path, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_property_message_names_the_property_and_its_location_without_exception_text()
    {
        const string Json = """{ "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text", "name": "total" }] }""";
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Json);
        var exception = Assert.ThrowsAny<JsonException>(
            () => JsonDocument.Parse(Json, new JsonDocumentOptions { AllowDuplicateProperties = false }));

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.InvalidJson, "order.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("The file is not valid JSON: property 'name' appears more than once at /fields/0.", diagnostic.Message);
        Assert.DoesNotContain(exception.Message, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_syntax_error_message_names_no_absolute_path()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", "{ \"kind\": ");

        var diagnostic = Assert.Single(ApplicationLoader.Load(folder.Path).Diagnostics);

        Assert.Equal((DiagnosticCodes.InvalidJson, "order.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(folder.Path, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_folder_is_reported_as_unlistable_without_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"axis-config-{Guid.NewGuid():N}");

        var result = ApplicationLoader.Load(path);

        AssertUnlistable(result, path);
    }

    // Skipped on Windows before any Unix-only call is made.
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Application_folder_that_cannot_be_listed_is_reported_as_unlistable()
    {
        SkipWhereFilePermissionsCannotDenyAccess();
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", OrderEntity)
            .DenyAccess("");

        var result = ApplicationLoader.Load(folder.Path);

        AssertUnlistable(result, folder.Path);
    }

    // Skipped on Windows before any Unix-only call is made.
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Subfolder_that_cannot_be_listed_fails_the_whole_load()
    {
        SkipWhereFilePermissionsCannotDenyAccess();
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", OrderEntity)
            .DenyAccess("entities");

        var result = ApplicationLoader.Load(folder.Path);

        AssertUnlistable(result, folder.Path);
    }

    [Fact]
    public void Entity_file_that_fails_validation_is_listed_as_unloaded_by_name()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("customer.json", """{ "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1 }""")
            .With("order.json", OrderEntity);

        var result = ApplicationLoader.Load(folder.Path);

        Assert.Equal(["Customer"], result.UnloadedEntityNames);
        Assert.Contains("CUSTOMER", result.UnloadedEntityNames);
        Assert.Equal("Order", Assert.Single(result.Entities).Name);
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

    private const string OrderEntity = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [{ "name": "number", "type": "text" }] }
        """;

    private static void AssertUnlistable(ApplicationLoadResult result, string folderPath)
    {
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnlistableFolder, "", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal("The application folder could not be listed.", diagnostic.Message);
        Assert.DoesNotContain(folderPath, diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.ManifestMissing);
        Assert.True(result.HasErrors);
        Assert.Null(result.Application);
        Assert.Empty(result.Entities);
        Assert.Empty(result.Resources);
    }

    private static void SkipWhereFilePermissionsCannotDenyAccess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File permissions are not Unix modes on Windows.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged process ignores file permissions.");
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
