using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class RaiseEventSensitiveTests
{
    private const string TicketId = "11111111-1111-4111-8111-111111111111";
    private const string AgentId = "11111111-1111-4111-8111-111111111112";
    private const string EventId = "f86ca48b-4afc-4060-9e40-c423522ea71d";
    private const string OtherEventId = "f86ca48b-4afc-4060-9e40-c423522ea71e";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string WebhookId = "e348a793-af68-42ce-83b5-336a9b10c802";

    [Theory]
    [InlineData("secret", "Ticket.secret")]
    [InlineData("assignee.salary", "Agent.salary")]
    public void A_field_that_reads_a_sensitive_field_into_an_event_with_a_webhook_is_reported_at_the_field(string expression, string field)
    {
        using var folder = Folder(expression).With("webhooks/ticket-closed.json", Webhook("TicketClosed"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SensitiveWebhookEventValue, "processes/ticket-close.json", "/steps/0/fields/detail"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{field}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'TicketClosed'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("assignee.salary")]
    public void The_same_step_compiles_when_no_webhook_names_the_event(string expression)
    {
        using var folder = Folder(expression);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("assignee.salary")]
    public void The_same_step_compiles_when_a_webhook_names_another_event(string expression)
    {
        using var folder = Folder(expression).With("webhooks/ticket-reopened.json", Webhook("TicketReopened"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_field_that_reads_a_field_that_is_not_sensitive_into_an_event_with_a_webhook_compiles()
    {
        using var folder = Folder("assignee.name").With("webhooks/ticket-closed.json", Webhook("TicketClosed"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_webhook_names_its_event_ignoring_letter_case()
    {
        using var folder = Folder("secret").With("webhooks/ticket-closed.json", Webhook("ticketclosed"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.SensitiveWebhookEventValue, diagnostic.Code);
    }

    private static string Webhook(string @event) => $$"""
        { "id": "{{WebhookId}}", "kind": "webhook", "name": "TicketChat", "formatVersion": 1, "event": "{{@event}}",
          "url": { "binding": "chatUrl" }, "secret": { "binding": "chatSecret" } }
        """;

    /// <summary>
    /// An application with the entities <c>Ticket</c>, which has a sensitive <c>secret</c> and an
    /// <c>assignee</c> reference to <c>Agent</c>, whose <c>salary</c> is sensitive. The events
    /// <c>TicketClosed</c> and <c>TicketReopened</c> carry a text <c>detail</c>. The process
    /// <c>TicketClose</c> raises <c>TicketClosed</c> with <paramref name="detail"/> for <c>detail</c>.
    /// </summary>
    private static TemporaryFolder Folder(string detail) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/ticket.json", $$"""
                { "id": "{{TicketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1,
                  "fields": [
                    { "name": "number", "type": "text" },
                    { "name": "secret", "type": "text", "sensitive": true },
                    { "name": "assignee", "type": "reference", "target": "Agent" }
                  ] }
                """)
            .With("entities/agent.json", $$"""
                { "id": "{{AgentId}}", "kind": "entity", "name": "Agent", "formatVersion": 1, "displayField": "name",
                  "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "salary", "type": "text", "sensitive": true }
                  ] }
                """)
            .With("events/ticket-closed.json", $$"""
                { "id": "{{EventId}}", "kind": "event", "name": "TicketClosed", "formatVersion": 1, "entity": "Ticket",
                  "fields": [ { "name": "detail", "type": "text" } ] }
                """)
            .With("events/ticket-reopened.json", $$"""
                { "id": "{{OtherEventId}}", "kind": "event", "name": "TicketReopened", "formatVersion": 1, "entity": "Ticket",
                  "fields": [ { "name": "detail", "type": "text" } ] }
                """)
            .With("processes/ticket-close.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "TicketClose", "formatVersion": 1,
                  "entity": "Ticket", "start": "raise", "steps": [
                    { "name": "raise", "type": "operation", "operation": "raiseEvent", "event": "TicketClosed",
                      "fields": { "detail": "{{detail}}" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """);
}
