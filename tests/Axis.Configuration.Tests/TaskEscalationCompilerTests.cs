using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class TaskEscalationCompilerTests
{
    private const string TicketId = "11111111-1111-4111-8111-111111111111";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string FormId = "99999999-9999-4999-8999-999999999901";

    private const string ValidNotify = """{ "to": [ { "role": "agent" }, { "user": "requester" } ], "channels": ["inApp"], "message": { "subject": { "textKey": "notice.subject" }, "body": { "textKey": "notice.body" }, "values": { "number": "number" } } }""";

    [Fact]
    public void Escalations_compile_into_the_model_in_file_order()
    {
        using var folder = Folder($$"""
            [
              { "name": "escalation1", "after": "PT4H", "notify": {{ValidNotify}} },
              { "name": "escalation2", "after": "P1D", "reassign": { "role": "lead" } }
            ]
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var task = Assert.IsType<TaskStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(2, task.Escalations.Count);

        var first = task.Escalations[0];
        Assert.Equal(TimeSpan.FromHours(4), first.After);
        Assert.Null(first.Reassign);
        Assert.NotNull(first.Notify);
        Assert.Equal(
            [((string?)null, "agent"), ("requester", null)],
            first.Notify.Recipients.Select(recipient => (recipient.User?.Expression, recipient.Role)));
        Assert.Equal(["inApp"], first.Notify.Channels);
        Assert.Equal(("notice.subject", "notice.body"), (first.Notify.Subject.TextKey, first.Notify.Body.TextKey));
        Assert.Equal([("number", "number")], first.Notify.Values.Select(value => (value.Name, value.Value.Expression)));
        Assert.Null(first.Notify.Open);

        var second = task.Escalations[1];
        Assert.Equal(TimeSpan.FromDays(1), second.After);
        Assert.Null(second.Notify);
        Assert.NotNull(second.Reassign);
        Assert.Equal("lead", second.Reassign.Role);
        Assert.Null(second.Reassign.User);
    }

    [Fact]
    public void A_task_without_escalations_has_an_empty_list_in_the_model()
    {
        using var folder = Folder(null);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Empty(Assert.IsType<TaskStepModel>(Assert.Single(result.Model.Processes).Steps[0]).Escalations);
    }

    [Fact]
    public void An_escalation_with_neither_reassign_nor_notify_is_reported_at_the_escalation()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P1D" } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.EscalationWithoutAction, "processes/ticket-task.json", "/steps/0/escalations/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_escalation_without_a_name_is_a_schema_violation_at_its_escalation()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P1D", "reassign": { "role": "lead" } }, { "after": "P2D", "reassign": { "role": "lead" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.File) == (DiagnosticCodes.SchemaViolation, "/steps/0/escalations/1", "processes/ticket-task.json"));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Two_escalations_with_the_same_name_ignoring_letter_case_are_a_duplicate_on_the_later_escalation()
    {
        using var folder = Folder("""[ { "name": "remind", "after": "P1D", "reassign": { "role": "lead" } }, { "name": "REMIND", "after": "P2D", "reassign": { "role": "lead" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateProcessElementName, "processes/ticket-task.json", "/steps/0/escalations/1/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'/steps/0/escalations/0'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("P5D", 0)]
    [InlineData("P6D", 0)]
    [InlineData("P5D", 1)]
    public void An_escalation_that_is_not_shorter_than_the_deadline_is_reported_at_its_after(string after, int index)
    {
        var escalations = index == 0
            ? $$"""[ { "name": "escalation1", "after": "{{after}}", "reassign": { "role": "lead" } } ]"""
            : $$"""[ { "name": "escalation1", "after": "P1D", "reassign": { "role": "lead" } }, { "name": "escalation2", "after": "{{after}}", "reassign": { "role": "lead" } } ]""";
        using var folder = Folder(escalations);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.EscalationNotBeforeDeadline, "processes/ticket-task.json", $"/steps/0/escalations/{index}/after"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_escalation_without_a_deadline_has_nothing_to_be_shorter_than()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P30D", "reassign": { "role": "lead" } } ]""", deadline: null);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var escalation = Assert.Single(Assert.IsType<TaskStepModel>(Assert.Single(result.Model.Processes).Steps[0]).Escalations);
        Assert.Equal(TimeSpan.FromDays(30), escalation.After);
    }

    [Theory]
    [InlineData("P1M")]
    [InlineData("P0D")]
    [InlineData("soon")]
    public void An_escalation_after_that_is_not_a_positive_fixed_duration_is_reported_at_its_after(string after)
    {
        using var folder = Folder($$"""[ { "name": "escalation1", "after": "{{after}}", "reassign": { "role": "lead" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidTaskDueIn, "processes/ticket-task.json", "/steps/0/escalations/0/after"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{after}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_value_that_reads_a_sensitive_field_in_an_escalation_notify_is_reported_at_that_value()
    {
        var notify = ValidNotify.Replace("\"number\": \"number\"", "\"number\": \"requester\"", StringComparison.Ordinal);
        using var folder = Folder($$"""[ { "name": "escalation1", "after": "PT4H", "notify": {{notify}} } ]""")
            .With("entities/ticket.json", $$"""
                { "id": "{{TicketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1,
                  "fields": [
                    { "name": "number", "type": "text" },
                    { "name": "requester", "type": "text", "sensitive": true }
                  ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SensitiveNotifyValue, "processes/ticket-task.json", "/steps/0/escalations/0/notify/message/values/number"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("Ticket.requester", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_placeholder_without_a_value_in_an_escalation_notify_is_reported_at_its_message()
    {
        var notify = ValidNotify.Replace(""", "values": { "number": "number" }""", "", StringComparison.Ordinal);
        using var folder = Folder($$"""[ { "name": "escalation1", "after": "PT4H", "notify": {{notify}} } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingPlaceholderValue, "processes/ticket-task.json", "/steps/0/escalations/0/notify/message"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'{number}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_reassign_role_that_no_role_declares_is_reported_at_its_role()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P1D", "reassign": { "role": "ghost" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownTaskRole, "processes/ticket-task.json", "/steps/0/escalations/0/reassign/role"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_reassign_with_both_user_and_role_is_reported_at_the_reassign()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P1D", "reassign": { "role": "lead", "user": "requester" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidTaskAssignee, "/steps/0/escalations/0/reassign"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_notify_that_needs_a_recipient_is_reported_at_its_to()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "PT4H", "notify": { "to": [], "channels": ["inApp"], "message": { "subject": { "textKey": "notice.subject" }, "body": { "textKey": "notice.body" }, "values": { "number": "number" } } } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.EmptyNotifyRecipients, "/steps/0/escalations/0/notify/to"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Escalations_may_come_in_any_order_of_after()
    {
        using var folder = Folder("""[ { "name": "escalation1", "after": "P2D", "reassign": { "role": "lead" } }, { "name": "escalation2", "after": "P1D", "reassign": { "role": "lead" } }, { "name": "escalation3", "after": "P1D", "reassign": { "role": "agent" } } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var task = Assert.IsType<TaskStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal([TimeSpan.FromDays(2), TimeSpan.FromDays(1), TimeSpan.FromDays(1)], task.Escalations.Select(escalation => escalation.After));
    }

    /// <summary>
    /// An application with a <c>Ticket</c> entity, the roles <c>agent</c> and <c>lead</c>, a form over
    /// <c>Ticket</c>, and a process <c>TicketTask</c> over <c>Ticket</c> with a task step <c>resolve</c>
    /// that leads to <c>done</c>. The arguments replace the <c>escalations</c> and the <c>deadline</c>
    /// of the task, which is written as a JSON array or object, or null to leave it out.
    /// </summary>
    private static TemporaryFolder Folder(string? escalations, string? deadline = """{ "after": "P5D", "next": "done" }""")
    {
        var process = $$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "TicketTask", "formatVersion": 1,
              "entity": "Ticket", "start": "resolve", "steps": [
                { "name": "resolve", "type": "task", "label": { "textKey": "task.label" }, "assignee": { "role": "agent" }, "form": "TicketForm",
                  "outcomes": [ { "name": "close", "label": { "textKey": "task.close" }, "next": "done" } ],
                  {{(deadline is null ? "" : $"\"deadline\": {deadline},")}}
                  {{(escalations is null ? "" : $"\"escalations\": {escalations},")}}
                  "dueIn": "P2D" },
                { "name": "done", "type": "end" }
              ] }
            """;
        return new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", """
                { "id": "55555555-5555-4555-8555-555555555e01", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": {
                    "ticket.title": "Ticket",
                    "role.agent": "Agent",
                    "role.lead": "Lead",
                    "task.label": "Resolve the ticket",
                    "task.close": "Close",
                    "notice.subject": "Ticket {number}",
                    "notice.body": "Ticket {number} is late."
                  } }
                """)
            .With("roles/agent.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "agent", "formatVersion": 1, "label": { "textKey": "role.agent" } }
                """)
            .With("roles/lead.json", """
                { "id": "44444444-4444-4444-8444-444444444402", "kind": "role", "name": "lead", "formatVersion": 1, "label": { "textKey": "role.lead" } }
                """)
            .With("entities/ticket.json", $$"""
                { "id": "{{TicketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1,
                  "fields": [
                    { "name": "number", "type": "text" },
                    { "name": "requester", "type": "text" }
                  ] }
                """)
            .With("forms/ticket-form.json", $$"""
                { "id": "{{FormId}}", "kind": "form", "name": "TicketForm", "formatVersion": 1, "entity": "Ticket",
                  "sections": [ { "title": { "textKey": "ticket.title" }, "fields": [ { "field": "number" } ] } ] }
                """)
            .With("processes/ticket-task.json", process);
    }
}
