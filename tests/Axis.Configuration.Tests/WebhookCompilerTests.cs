using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class WebhookCompilerTests
{
    private const string WebhookId = "e348a793-af68-42ce-83b5-336a9b10c801";
    private const string NoteId = "11111111-1111-4111-8111-111111111111";
    private const string NoteClosedId = "f86ca48b-4afc-4060-9e40-c423522ea71d";
    private const string DepartmentId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c31";
    private const string DepartmentRenamedId = "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c32";
    private const string WebhookFile = "webhooks/note-closed.json";

    private const string Binding = """{ "binding": "chat" }""";

    [Fact]
    public void A_valid_webhook_compiles_into_the_model_with_its_bindings_and_the_default_timeout()
    {
        using var folder = Folder().With(WebhookFile, Webhook("noteclosed"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var webhook = Assert.Single(result.Model.Webhooks);
        Assert.Equal(
            (Guid.Parse(WebhookId), "NoteClosedChat", WebhookFile, "chatUrl", "chatSecret", TimeSpan.FromSeconds(10)),
            (webhook.Id, webhook.Name, webhook.File, webhook.UrlBinding, webhook.SecretBinding, webhook.Timeout));

        // The event resolves ignoring letter case to its declared name.
        Assert.Equal(new EventReference(Guid.Parse(NoteClosedId), "NoteClosed"), webhook.Event);
        Assert.Contains(result.Resources, resource => resource.Path == WebhookFile);
    }

    [Fact]
    public void A_webhook_timeout_up_to_thirty_seconds_is_kept()
    {
        using var folder = Folder().With(WebhookFile, Webhook("NoteClosed", timeout: "PT30S"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(result.Model!.Webhooks).Timeout);
    }

    [Fact]
    public void A_webhook_on_the_event_of_a_declared_module_compiles()
    {
        using var modules = Modules();
        using var folder = Folder(declareModule: true).With(WebhookFile, Webhook("departments/DepartmentRenamed"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var webhook = Assert.Single(result.Model.Webhooks);
        var @event = Assert.Single(result.Model.Events, candidate => candidate.Name == "departments/DepartmentRenamed");
        Assert.Equal(new EventReference(@event.Id, "departments/DepartmentRenamed"), webhook.Event);
    }

    [Fact]
    public void A_webhook_on_the_event_of_a_module_the_application_does_not_declare_is_reported_at_the_event()
    {
        using var modules = Modules();
        using var folder = Folder().With(WebhookFile, Webhook("departments/DepartmentRenamed"));

        var result = ApplicationCompiler.Compile(folder.Path, null, ModuleCatalog.Load(modules.Path).Modules);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, WebhookFile, "/event"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_webhook_on_an_unknown_event_is_reported_at_the_event()
    {
        using var folder = Folder().With(WebhookFile, Webhook("NoteOpened"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWebhookEvent, WebhookFile, "/event", Guid.Parse(WebhookId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'NoteOpened'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_webhook_on_an_event_whose_file_did_not_load_adds_no_report_of_its_own()
    {
        using var folder = Folder()
            .With("events/note-opened.json", """{ "id": "f86ca48b-4afc-4060-9e40-c423522ea71f", "kind": "event", "name": "NoteOpened", "formatVersion": 1 }""")
            .With(WebhookFile, Webhook("NoteOpened"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.UnknownWebhookEvent);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.File == "events/note-opened.json");
    }

    [Fact]
    public void A_webhook_without_a_url_or_a_secret_is_reported_at_each_missing_binding()
    {
        using var folder = Folder().With(WebhookFile, $$"""
            { "id": "{{WebhookId}}", "kind": "webhook", "name": "NoteClosedChat", "formatVersion": 1, "event": "NoteClosed" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [(DiagnosticCodes.MissingWebhookBinding, "/secret"), (DiagnosticCodes.MissingWebhookBinding, "/url")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order());
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(WebhookFile, diagnostic.File));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("PT31S")]
    [InlineData("PT0S")]
    [InlineData("PT1M")]
    [InlineData("ten seconds")]
    public void A_webhook_timeout_out_of_range_is_reported_at_the_timeout(string timeout)
    {
        using var folder = Folder().With(WebhookFile, Webhook("NoteClosed", timeout: timeout));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWebhookTimeout, WebhookFile, "/timeout"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Every_problem_of_a_webhook_is_reported_in_one_pass()
    {
        using var folder = Folder().With(WebhookFile, $$"""
            { "id": "{{WebhookId}}", "kind": "webhook", "name": "NoteClosedChat", "formatVersion": 1,
              "event": "NoteOpened", "timeout": "PT31S" }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownWebhookEvent, "/event"),
                (DiagnosticCodes.MissingWebhookBinding, "/secret"),
                (DiagnosticCodes.MissingWebhookBinding, "/url"),
                (DiagnosticCodes.InvalidWebhookTimeout, "/timeout"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order());
    }

    private static string Webhook(string @event, string? timeout = null) => $$"""
        { "id": "{{WebhookId}}", "kind": "webhook", "name": "NoteClosedChat", "formatVersion": 1, "event": "{{@event}}",
          "url": { "binding": "chatUrl" }, "secret": { "binding": "chatSecret" }
          {{(timeout is null ? "" : $", \"timeout\": \"{timeout}\"")}} }
        """;

    /// <summary>An application with the entity <c>Note</c> and the event <c>NoteClosed</c> about it.</summary>
    private static TemporaryFolder Folder(bool declareModule = false) =>
        new TemporaryFolder()
            .With("application.json", declareModule
                ? """
                  { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1,
                    "modules": [{ "id": "departments", "version": "1.0.0" }] }
                  """
                : PresentationCompilerTests.Manifest)
            .With("entities/note.json", $$"""
                { "id": "{{NoteId}}", "kind": "entity", "name": "Note", "formatVersion": 1,
                  "fields": [ { "name": "title", "type": "text" } ] }
                """)
            .With("events/note-closed.json", $$"""
                { "id": "{{NoteClosedId}}", "kind": "event", "name": "NoteClosed", "formatVersion": 1, "entity": "Note",
                  "fields": [ { "name": "title", "type": "text" } ] }
                """);

    private static TemporaryFolder Modules() =>
        new TemporaryFolder()
            .With("departments/1.0.0/module.json", """{ "kind": "module", "id": "departments", "version": "1.0.0", "label": "Departments" }""")
            .With("departments/1.0.0/Department.json", $$"""
                { "id": "{{DepartmentId}}", "kind": "entity", "name": "Department", "formatVersion": 1,
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("departments/1.0.0/DepartmentRenamed.json", $$"""
                { "id": "{{DepartmentRenamedId}}", "kind": "event", "name": "DepartmentRenamed", "formatVersion": 1, "entity": "Department",
                  "fields": [ { "name": "name", "type": "text" } ] }
                """);
}
