using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class NotifyStepCompilerTests
{
    private const string TicketId = "11111111-1111-4111-8111-111111111111";
    private const string ProcessId = "77777777-7777-4777-8777-777777777701";
    private const string DetailPageId = "55555555-5555-4555-8555-555555555501";

    private const string ValidTo = """[ { "user": "requester" }, { "role": "agent" }, { "email": "requester" } ]""";
    private const string ValidChannels = """["inApp"]""";
    private const string ValidValues = """{ "number": "number", "resolution": "resolution" }""";

    [Fact]
    public void A_valid_notify_step_with_open_compiles_into_the_model()
    {
        using var folder = Folder(
            to: ValidTo,
            channels: """["inApp"]""",
            values: """{ "resolution": "resolution", "priority": "priority", "number": "number" }""",
            open: """{ "page": "TicketDetail", "parameters": { "TICKET": "number" } }""",
            enSubject: "Ticket {number} is {resolution}",
            enBody: "Priority {priority}. A literal {{brace}} stays.",
            viSubject: "Phiếu {number} {resolution}",
            viBody: "Ưu tiên {priority}.");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        var notify = Assert.IsType<NotifyStepModel>(process.Steps[0]);
        Assert.Equal(("tell", "notice.subject", "notice.body", "done"), (notify.Name, notify.Subject.TextKey, notify.Body.TextKey, notify.Next));
        Assert.Equal(["inApp"], notify.Channels);
        Assert.Equal(
            [("requester", null, null), (null, "agent", null), (null, null, "requester")],
            notify.Recipients.Select(recipient => (recipient.User?.Expression, recipient.Role, recipient.Email?.Expression)));
        Assert.Equal(
            [("number", "number"), ("priority", "priority"), ("resolution", "resolution")],
            notify.Values.Select(value => (value.Name, value.Value.Expression)));
        Assert.NotNull(notify.Open);
        Assert.Equal(new PageReference(Guid.Parse(DetailPageId), "TicketDetail"), notify.Open.Page);
        var parameter = Assert.Single(notify.Open.Parameters);
        Assert.Equal(("ticket", "number"), (parameter.Parameter, parameter.Value.Expression));
    }

    [Fact]
    public void The_channels_of_a_notify_step_keep_their_file_order()
    {
        using var folder = Folder(to: """[ { "email": "requester" } ]""", channels: """["email", "inApp"]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var notify = Assert.IsType<NotifyStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Equal(["email", "inApp"], notify.Channels);
    }

    [Fact]
    public void A_notify_step_without_open_has_no_open_in_the_model()
    {
        using var folder = Folder();

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var notify = Assert.IsType<NotifyStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Null(notify.Open);
    }

    [Theory]
    [InlineData("[]", ValidChannels, DiagnosticCodes.EmptyNotifyRecipients, "/steps/0/to")]
    [InlineData("""[ { "user": "requester" }, { "user": "requester", "email": "requester" } ]""", ValidChannels, DiagnosticCodes.InvalidNotifyRecipient, "/steps/0/to/1")]
    [InlineData("""[ { } ]""", ValidChannels, DiagnosticCodes.InvalidNotifyRecipient, "/steps/0/to/0")]
    [InlineData("""[ { "role": "ghost" } ]""", ValidChannels, DiagnosticCodes.UnknownNotifyRole, "/steps/0/to/0/role")]
    [InlineData("""[ { "role": "Agent" } ]""", ValidChannels, DiagnosticCodes.UnknownNotifyRole, "/steps/0/to/0/role")]
    [InlineData(ValidTo, "[]", DiagnosticCodes.EmptyNotifyChannels, "/steps/0/channels")]
    [InlineData("""[ { "role": "agent" } ]""", """["email"]""", DiagnosticCodes.RoleRecipientWithEmail, "/steps/0/to/0/role")]
    public void A_bad_recipient_or_channel_list_is_reported_at_its_path(string to, string channels, string code, string path)
    {
        using var folder = Folder(to: to, channels: channels);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "processes/ticket-notice.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_duplicate_channel_is_a_schema_violation()
    {
        using var folder = Folder(channels: """["inApp", "inApp"]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_placeholder_without_a_value_in_the_vi_text_is_reported_at_the_message()
    {
        using var folder = Folder(viBody: "Giải pháp: {resolution}, người gửi {requester}.");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingPlaceholderValue, "processes/ticket-notice.json", "/steps/0/message"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'{requester}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'vi'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_placeholder_name_is_matched_with_its_letter_case()
    {
        using var folder = Folder(enSubject: "Ticket {Number}");

        var result = ApplicationCompiler.Compile(folder.Path);

        // '{Number}' has no value, and the value 'number' then has no placeholder in the en text.
        Assert.Equal(
            [DiagnosticCodes.MissingPlaceholderValue, DiagnosticCodes.UnusedPlaceholderValue],
            result.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_value_with_no_placeholder_is_reported_at_the_message()
    {
        using var folder = Folder(values: """{ "number": "number", "resolution": "resolution", "extra": "number" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostics = result.Diagnostics.OrderBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToList();
        Assert.Equal(2, diagnostics.Count);
        Assert.All(
            diagnostics,
            diagnostic => Assert.Equal(
                (DiagnosticCodes.UnusedPlaceholderValue, "/steps/0/message"),
                (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("'extra'", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("'en'", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("'vi'", diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("Ticket { now")]
    [InlineData("Ticket {} now")]
    [InlineData("Ticket {a b}")]
    [InlineData("Ticket {a-b}")]
    [InlineData("Ticket } now")]
    [InlineData("Ticket {{number}")]
    [InlineData("Ticket {number}}")]
    public void A_lone_or_invalid_brace_is_reported_once_at_the_message(string enSubject)
    {
        using var folder = Folder(enSubject: enSubject);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidPlaceholder, "processes/ticket-notice.json", "/steps/0/message"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'en'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'notice.subject'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Doubled_braces_are_literal_text_and_not_placeholders()
    {
        using var folder = Folder(
            values: "{}",
            enSubject: "Use {{number}} as a literal",
            enBody: "Close with }} and open with {{",
            viSubject: "{{}} {{{{",
            viBody: "Không có chỗ giữ");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var notify = Assert.IsType<NotifyStepModel>(Assert.Single(result.Model.Processes).Steps[0]);
        Assert.Empty(notify.Values);
    }

    [Fact]
    public void A_recipient_expression_that_is_not_text_is_reported_at_the_recipient()
    {
        using var folder = Folder(to: """[ { "user": "priority" } ]""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/to/0/user"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_value_with_an_unknown_name_is_reported_at_the_value()
    {
        using var folder = Folder(values: """{ "number": "number", "resolution": "nothing" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("/steps/0/message/values/resolution", diagnostic.Path);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "page": "Missing" }""", DiagnosticCodes.UnknownNavigatePage, "/steps/0/open/page")]
    [InlineData("""{ "page": "TicketDetail" }""", DiagnosticCodes.MissingNavigateParameter, "/steps/0/open")]
    [InlineData("""{ "page": "TicketDetail", "parameters": { "ticket": "number", "other": "number" } }""", DiagnosticCodes.UnknownNavigateParameter, "/steps/0/open/parameters/other")]
    [InlineData("""{ "page": "TicketDetail", "parameters": { "ticket": "priority" } }""", ExpressionDiagnosticCodes.ResultTypeMismatch, "/steps/0/open/parameters/ticket")]
    public void A_bad_open_is_reported_as_a_navigate_action_is(string open, string code, string path)
    {
        using var folder = Folder(open: open);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, path), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_second_text_file_for_a_locale_is_a_duplicate_locale_and_does_not_crash_the_compile()
    {
        using var folder = Folder()
            .With("texts/en-copy.json", TextFile("55555555-5555-4555-8555-555555555e03", "TextsEnCopy", "EN", "Ticket {number}", "Resolution: {resolution}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.DuplicateLocale, diagnostic.Code);
    }

    [Fact]
    public void A_missing_text_key_is_reported_at_the_key_and_skips_the_placeholder_check()
    {
        using var folder = Folder(subjectKey: "notice.missing");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "/steps/0/message/subject/textKey"),
            (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Notify_properties_on_another_operation_are_a_schema_violation()
    {
        using var folder = Folder(operation: "updateRecord");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.SchemaViolation);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("{a}", new string[] { "a" })]
    [InlineData("a", new string[0])]
    [InlineData("{a}{b}{a}", new string[] { "a", "b", "a" })]
    [InlineData("{{a}}", new string[0])]
    [InlineData("{{{a}}}", new string[] { "a" })]
    [InlineData("x {A1} y", new string[] { "A1" })]
    [InlineData("", new string[0])]
    public void The_parser_gives_the_placeholders_of_a_valid_text(string text, string[] expected)
    {
        Assert.True(TextPlaceholders.TryParse(text, out var names, out var problem));

        Assert.Equal(expected, names);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("{", "'{' at offset 0")]
    [InlineData("ab}", "'}' at offset 2")]
    [InlineData("{é}", "'{' at offset 0")]
    [InlineData("{a", "'{' at offset 0")]
    public void The_parser_names_the_brace_and_offset_of_a_bad_text(string text, string expected)
    {
        Assert.False(TextPlaceholders.TryParse(text, out _, out var problem));

        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// An application with a <c>Ticket</c> entity, the role <c>agent</c>, the page <c>TicketDetail</c> with a
    /// required text parameter <c>ticket</c>, and a process <c>TicketNotice</c> over <c>Ticket</c> with a notify
    /// step <c>tell</c> that leads to <c>done</c>. The arguments replace the parts of the step and the
    /// <c>en</c> and <c>vi</c> texts. The default texts use the placeholders <c>number</c> and <c>resolution</c>.
    /// </summary>
    private static TemporaryFolder Folder(
        string to = ValidTo,
        string channels = ValidChannels,
        string values = ValidValues,
        string? open = null,
        string operation = "notify",
        string subjectKey = "notice.subject",
        string enSubject = "Ticket {number}",
        string enBody = "Resolution: {resolution}",
        string viSubject = "Phiếu {number}",
        string viBody = "Giải pháp: {resolution}")
    {
        var process = $$"""
            { "id": "{{ProcessId}}", "kind": "process", "name": "TicketNotice", "formatVersion": 1,
              "entity": "Ticket", "start": "tell", "steps": [
                { "name": "tell", "type": "operation", "operation": "{{operation}}", "to": {{to}}, "channels": {{channels}},
                  "message": { "subject": { "textKey": "{{subjectKey}}" }, "body": { "textKey": "notice.body" }, "values": {{values}} },
                  {{(open is null ? "" : $"\"open\": {open},")}} "next": "done" },
                { "name": "done", "type": "end" }
              ] }
            """;
        return new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", TextFile("55555555-5555-4555-8555-555555555e01", "TextsEn", "en", enSubject, enBody))
            .With("texts/vi.json", TextFile("55555555-5555-4555-8555-555555555e02", "TextsVi", "vi", viSubject, viBody))
            .With("roles/agent.json", """
                { "id": "44444444-4444-4444-8444-444444444401", "kind": "role", "name": "agent", "formatVersion": 1, "label": { "textKey": "role.agent" } }
                """)
            .With("entities/ticket.json", $$"""
                { "id": "{{TicketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1,
                  "fields": [
                    { "name": "number", "type": "text" },
                    { "name": "resolution", "type": "text" },
                    { "name": "requester", "type": "text" },
                    { "name": "priority", "type": "integer" }
                  ] }
                """)
            .With("pages/ticket-detail.json", PresentationCompilerTests.Page(
                "TicketDetail",
                """{ "type": "taskInbox" }""",
                DetailPageId,
                "ticket.title",
                """{ "name": "ticket", "type": "text", "required": true }"""))
            .With("processes/ticket-notice.json", process);
    }

    private static string TextFile(string id, string name, string locale, string subject, string body) =>
        $$"""
        { "id": "{{id}}", "kind": "text", "name": "{{name}}", "formatVersion": 1, "locale": "{{locale}}",
          "texts": {
            "ticket.title": "Ticket",
            "role.agent": "Agent",
            "notice.subject": {{System.Text.Json.JsonSerializer.Serialize(subject)}},
            "notice.body": {{System.Text.Json.JsonSerializer.Serialize(body)}}
          } }
        """;
}
