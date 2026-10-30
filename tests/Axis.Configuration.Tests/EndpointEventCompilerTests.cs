using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class EndpointEventCompilerTests
{
    private const string EndpointId = "83244ba2-b26f-4348-aa7a-0e39eb992fd3";
    private const string TicketId = "11111111-1111-4111-8111-111111111111";
    private const string AgentId = "11111111-1111-4111-8111-111111111112";
    private const string EventId = "f86ca48b-4afc-4060-9e40-c423522ea71d";
    private const string AgentEventId = "f86ca48b-4afc-4060-9e40-c423522ea71e";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string WebhookId = "e348a793-af68-42ce-83b5-336a9b10c802";

    private const string Input = """
        [
          { "name": "title", "type": "text" },
          { "name": "token", "type": "text" },
          { "name": "cost", "type": "decimal", "precision": 18, "scale": 2 },
          { "name": "who", "type": "reference", "target": "Agent" }
        ]
        """;

    private const string Values = """{ "title": "title", "secret": "token", "amount": "cost" }""";

    [Fact]
    public void Event_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder()
            .With("endpoints/unknown.json", Endpoint("Unknown", """{ "name": "Nope", "fields": {} }""", "11"))
            .With("endpoints/other.json", Endpoint("Other", """{ "name": "AgentJoined", "fields": { "name": "title" } }""", "12"))
            .With("endpoints/payload.json", Endpoint(
                "Payload", """{ "name": "TicketLogged", "fields": { "nope": "title", "detail": "title", "DETAIL": "title" } }""", "13"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                ("endpoints/other.json", DiagnosticCodes.StepEventOverOtherEntity, "/event/name"),
                ("endpoints/payload.json", DiagnosticCodes.MissingEventPayloadField, "/event/fields"),
                ("endpoints/payload.json", DiagnosticCodes.UnknownEventPayloadField, "/event/fields/DETAIL"),
                ("endpoints/payload.json", DiagnosticCodes.UnknownEventPayloadField, "/event/fields/nope"),
                ("endpoints/unknown.json", DiagnosticCodes.UnknownStepEvent, "/event/name"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.Code, diagnostic.Path)));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("NeedsReview(cost)")]
    [InlineData("sum(rows, 1)")]
    [InlineData("who.name")]
    [InlineData("cost")]
    public void An_expression_that_cannot_give_the_payload_field_is_reported_at_its_key(string expression)
    {
        using var folder = Folder().With("endpoints/log.json", Endpoint(
            "Log", $$"""{ "name": "TicketLogged", "fields": { "detail": "{{expression}}", "total": "cost" } }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("endpoints/log.json", "/event/fields/detail"), (diagnostic.File, diagnostic.Path));
        Assert.Equal(Expected(expression), diagnostic.Code);
        Assert.Null(result.Model);
    }

    private static string Expected(string expression) => expression switch
    {
        "NeedsReview(cost)" => ExpressionDiagnosticCodes.UnknownFunction,
        "sum(rows, 1)" or "who.name" => ExpressionDiagnosticCodes.UnknownName,
        _ => ExpressionDiagnosticCodes.ResultTypeMismatch,
    };

    [Theory]
    [InlineData("token")]
    [InlineData("upper(token)")]
    [InlineData("concat(title, token)")]
    public void An_expression_that_reads_an_input_mapped_to_a_sensitive_field_is_reported_when_a_webhook_names_the_event(string expression)
    {
        using var folder = Folder()
            .With("endpoints/log.json", Endpoint("Log", Event(expression)))
            .With("webhooks/ticket-logged.json", Webhook("TicketLogged"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SensitiveWebhookEventValue, "endpoints/log.json", "/event/fields/detail"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'token'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'Ticket.secret'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'TicketLogged'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("token", false)]
    [InlineData("upper(token)", false)]
    [InlineData("title", true)]
    [InlineData("title", false)]
    public void The_same_expression_compiles_without_a_webhook_or_when_it_reads_no_sensitive_input(string expression, bool withWebhook)
    {
        using var folder = Folder().With("endpoints/log.json", Endpoint("Log", Event(expression)));
        if (withWebhook)
        {
            folder.With("webhooks/ticket-logged.json", Webhook("TicketLogged"));
        }

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void A_compiled_endpoint_holds_the_event_and_its_payload_in_the_events_declaration_order()
    {
        using var folder = Folder().With("endpoints/log.json", Endpoint(
            "Log", """{ "name": "ticketlogged", "fields": { "TOTAL": "cost * 2", "detail": "title" } }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var raised = Assert.Single(result.Model!.Endpoints).Event;
        Assert.NotNull(raised);
        Assert.Equal("TicketLogged", raised.Name);
        Assert.Equal(["detail", "total"], raised.Fields.Select(field => field.Field));
    }

    [Fact]
    public void An_endpoint_without_an_event_holds_none()
    {
        using var folder = Folder().With("endpoints/log.json", Endpoint("Log", null));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Null(Assert.Single(result.Model!.Endpoints).Event);
    }

    [Fact]
    public void The_event_is_checked_when_the_entity_is_unknown_and_its_expressions_wait_for_a_valid_input()
    {
        using var folder = Folder()
            .With("endpoints/no-entity.json", Endpoint("NoEntity", """{ "name": "Nope", "fields": {} }""", "11", entity: "Nothing"))
            .With("endpoints/bad-input.json", Endpoint(
                "BadInput", """{ "name": "TicketLogged", "fields": { "detail": "missing", "total": "1" } }""", "12", input: """[{ "name": "state", "type": "enum" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                ("endpoints/bad-input.json", DiagnosticCodes.MissingTypeProperty, "/input/0"),
                ("endpoints/no-entity.json", DiagnosticCodes.UnknownEndpointEntity, "/entity"),
                ("endpoints/no-entity.json", DiagnosticCodes.UnknownStepEvent, "/event/name"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void An_event_under_an_undeclared_module_is_reported_at_its_name()
    {
        using var folder = Folder().With("endpoints/log.json", Endpoint("Log", """{ "name": "tickets/TicketLogged", "fields": {} }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UndeclaredModuleReference, "/event/name"),
            (diagnostic.Code, diagnostic.Path));
    }

    private static string Event(string detail) => $$"""{ "name": "TicketLogged", "fields": { "detail": "{{detail}}", "total": "cost" } }""";

    private static string Webhook(string @event) => $$"""
        { "id": "{{WebhookId}}", "kind": "webhook", "name": "TicketChat", "formatVersion": 1, "event": "{{@event}}",
          "url": { "binding": "chatUrl" }, "secret": { "binding": "chatSecret" } }
        """;

    private static string Endpoint(string name, string? @event, string id = "13", string entity = "Ticket", string input = Input) => $$"""
        { "id": "83244ba2-b26f-4348-aa7a-0e39eb9920{{id}}", "kind": "endpoint", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}",
          "process": "TicketClose", {{(@event is null ? "" : $"\"event\": {@event},")}} "input": {{input}}, "values": {{Values}} }
        """;

    /// <summary>
    /// An application with the root entity <c>Ticket</c>, which has a sensitive <c>secret</c> and a
    /// decimal <c>amount</c>, the root entity <c>Agent</c>, the events <c>TicketLogged</c>, with a
    /// text <c>detail</c> and then a decimal <c>total</c>, and <c>AgentJoined</c>, the process
    /// <c>TicketClose</c> over <c>Ticket</c> and the rule <c>NeedsReview</c>.
    /// </summary>
    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/ticket.json", $$"""
                { "id": "{{TicketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1, "displayField": "title",
                  "fields": [
                    { "name": "title", "type": "text", "required": true },
                    { "name": "secret", "type": "text", "sensitive": true },
                    { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 }
                  ] }
                """)
            .With("entities/agent.json", $$"""
                { "id": "{{AgentId}}", "kind": "entity", "name": "Agent", "formatVersion": 1, "displayField": "name",
                  "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("events/ticket-logged.json", $$"""
                { "id": "{{EventId}}", "kind": "event", "name": "TicketLogged", "formatVersion": 1, "entity": "Ticket",
                  "fields": [ { "name": "detail", "type": "text" }, { "name": "total", "type": "decimal" } ] }
                """)
            .With("events/agent-joined.json", $$"""
                { "id": "{{AgentEventId}}", "kind": "event", "name": "AgentJoined", "formatVersion": 1, "entity": "Agent",
                  "fields": [ { "name": "name", "type": "text" } ] }
                """)
            .With("rules/needs-review.json", """
                { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "NeedsReview", "formatVersion": 1,
                  "parameters": [{ "name": "total", "type": "decimal" }], "resultType": "boolean", "expression": "total >= 10000" }
                """)
            .With("processes/ticket-close.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "TicketClose", "formatVersion": 1, "entity": "Ticket",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """);
}
