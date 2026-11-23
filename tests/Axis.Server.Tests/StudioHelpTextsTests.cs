using System.Text.RegularExpressions;
using Axis.Presentation.Texts;

namespace Axis.Server.Tests;

/// <summary>
/// Pins the help rules of docs/reference/ui-writing.md#kinds-of-text on the English texts of Studio's resource forms, and
/// the Vietnamese rules of docs/reference/ui-writing.md#vietnamese on the Vietnamese ones.
/// A rule is pinned by the shape of its texts, not by their exact words.
/// </summary>
public sealed class StudioHelpTextsTests
{
    private readonly IReadOnlyDictionary<string, string> _en = new EmbeddedTextResourceProvider().GetTexts("en")!.Texts;
    private readonly IReadOnlyDictionary<string, string> _vi = new EmbeddedTextResourceProvider().GetTexts("vi")!.Texts;

    [Theory]
    [InlineData("shell.studio.field.webhook.timeout.help")]
    [InlineData("shell.studio.field.endpoint.wait.help")]
    [InlineData("shell.studio.field.process.steps.dueIn.help")]
    [InlineData("shell.studio.field.process.steps.deadline.after.help")]
    [InlineData("shell.studio.field.process.steps.escalations.after.help")]
    [InlineData("shell.studio.field.process.steps.wait.help")]
    [InlineData("shell.studio.field.process.steps.timeout.after.help")]
    [InlineData("shell.studio.field.connector.timeout.help")]
    [InlineData("shell.studio.field.scenario.now.help")]
    [InlineData("shell.studio.field.scenario.steps.by.help")]
    public void An_example_says_what_it_means(string key)
    {
        var text = Text(key);
        var example = text.IndexOf("such as ", StringComparison.Ordinal);

        Assert.True(example >= 0, $"{key} gives no example: {text}");
        Assert.Contains(" for ", text[example..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shell.studio.field.form.sections.fields.visibleWhen.help")]
    [InlineData("shell.studio.field.form.sections.fields.requiredWhen.help")]
    [InlineData("shell.studio.field.form.sections.fields.readOnlyWhen.help")]
    [InlineData("shell.studio.field.form.sections.visibleWhen.help")]
    [InlineData("shell.studio.field.page.widgets.rowActions.visibleWhen.help")]
    [InlineData("shell.studio.field.process.startCondition.expression.help")]
    [InlineData("shell.studio.field.process.steps.branches.when.help")]
    [InlineData("shell.studio.field.process.steps.when.help")]
    [InlineData("shell.studio.field.trigger.when.help")]
    public void A_condition_reads_as_a_condition(string key) =>
        Assert.StartsWith("When this expression is true", Text(key), StringComparison.Ordinal);

    [Theory]
    [InlineData("shell.studio.field.page.parameters.required.help")]
    [InlineData("shell.studio.field.page.widgets.collapsed.help")]
    [InlineData("shell.studio.field.dataSource.parameters.required.help")]
    [InlineData("shell.studio.field.dataSource.search.help")]
    [InlineData("shell.studio.field.seed.sync.help")]
    [InlineData("shell.studio.field.endpoint.input.required.help")]
    [InlineData("shell.studio.field.form.sections.fields.readOnly.help")]
    [InlineData("shell.studio.field.entity.fields.required.help")]
    [InlineData("shell.studio.field.entity.fields.unique.help")]
    [InlineData("shell.studio.field.entity.fields.userId.help")]
    [InlineData("shell.studio.field.entity.fields.searchable.help")]
    [InlineData("shell.studio.field.entity.fields.sensitive.help")]
    [InlineData("shell.studio.field.entity.fields.exactLookup.help")]
    [InlineData("shell.studio.field.entity.indexes.unique.help")]
    public void A_checkbox_says_what_turning_it_on_does(string key) =>
        Assert.StartsWith("Turn on so", Text(key), StringComparison.Ordinal);

    [Theory]
    [InlineData("shell.studio.field.page.widgets.entity.help")]
    [InlineData("shell.studio.field.page.widgets.dataSource.help")]
    [InlineData("shell.studio.field.page.widgets.form.help")]
    public void A_choice_that_clears_another_names_what_it_clears(string key) =>
        Assert.Contains("Choosing it clears the ", Text(key), StringComparison.Ordinal);

    [Fact]
    public void The_entity_of_a_widget_names_the_data_source_or_form_it_clears() =>
        Assert.Contains("the data source or form chosen before", Text("shell.studio.field.page.widgets.entity.help"), StringComparison.Ordinal);

    [Theory]
    [InlineData("shell.studio.field.application.label.help")]
    [InlineData("shell.studio.field.application.description.help")]
    [InlineData("shell.studio.field.role.label.help")]
    [InlineData("shell.studio.field.role.description.help")]
    [InlineData("shell.studio.field.page.title.help")]
    [InlineData("shell.studio.field.page.description.help")]
    [InlineData("shell.studio.field.page.widgets.rowActions.label.help")]
    [InlineData("shell.studio.field.page.widgets.toolbarActions.label.help")]
    [InlineData("shell.studio.field.page.widgets.actions.label.help")]
    [InlineData("shell.studio.field.page.widgets.navigate.label.help")]
    [InlineData("shell.studio.field.page.widgets.label.help")]
    [InlineData("shell.studio.field.page.widgets.comparison.label.help")]
    [InlineData("shell.studio.field.page.widgets.title.help")]
    [InlineData("shell.studio.field.page.widgets.measures.label.help")]
    [InlineData("shell.studio.field.page.widgets.tabs.label.help")]
    [InlineData("shell.studio.field.policy.description.help")]
    [InlineData("shell.studio.field.rule.description.help")]
    [InlineData("shell.studio.field.sequence.description.help")]
    [InlineData("shell.studio.field.event.description.help")]
    [InlineData("shell.studio.field.trigger.description.help")]
    [InlineData("shell.studio.field.dataSource.description.help")]
    [InlineData("shell.studio.field.dataSource.parameters.label.help")]
    [InlineData("shell.studio.field.webhook.description.help")]
    [InlineData("shell.studio.field.migration.description.help")]
    [InlineData("shell.studio.field.seed.description.help")]
    [InlineData("shell.studio.field.site.title.help")]
    [InlineData("shell.studio.field.site.description.help")]
    [InlineData("shell.studio.field.site.navigation.label.help")]
    [InlineData("shell.studio.field.endpoint.description.help")]
    [InlineData("shell.studio.field.form.description.help")]
    [InlineData("shell.studio.field.form.sections.title.help")]
    [InlineData("shell.studio.field.form.steps.title.help")]
    [InlineData("shell.studio.field.entity.label.help")]
    [InlineData("shell.studio.field.entity.description.help")]
    [InlineData("shell.studio.field.entity.validations.messageText.help")]
    [InlineData("shell.studio.field.process.title.help")]
    [InlineData("shell.studio.field.process.description.help")]
    [InlineData("shell.studio.field.process.startCondition.message.help")]
    [InlineData("shell.studio.field.process.steps.escalations.notify.message.subject.help")]
    [InlineData("shell.studio.field.process.steps.escalations.notify.message.body.help")]
    [InlineData("shell.studio.field.process.steps.message.subject.help")]
    [InlineData("shell.studio.field.process.steps.message.body.help")]
    [InlineData("shell.studio.field.connector.description.help")]
    [InlineData("shell.studio.field.identityProvider.description.help")]
    [InlineData("shell.studio.field.identityProvider.label.help")]
    [InlineData("shell.studio.field.scenario.description.help")]
    public void A_text_the_author_writes_ends_in_each_language_of_the_application(string key) =>
        Assert.EndsWith("in each language of the application.", Text(key), StringComparison.Ordinal);

    [Theory]
    [InlineData("shell.studio.field.form.sections.fields.width")]
    [InlineData("shell.studio.field.page.widgets.columns.width")]
    public void A_width_counts_twelfths_of_a_row(string key) => Assert.Equal("Width (of 12)", Text(key));

    [Fact]
    public void A_scenarios_fixed_time_names_its_event() => Assert.Equal("Runs at", Text("shell.studio.field.scenario.now"));

    [Fact]
    public void A_grant_setting_has_one_label_in_the_policy_form_and_the_matrix()
    {
        Assert.Equal("Record filter", Text("shell.studio.field.policy.grants.filter"));
        Assert.Equal("Fields", Text("shell.studio.field.policy.grants.fields"));
        Assert.Equal("Actions", Text("shell.studio.field.policy.grants.actions"));
        Assert.DoesNotContain(_en.Keys, key => key is "shell.studio.matrix.filter" or "shell.studio.matrix.fields" or "shell.studio.matrix.actions");
    }

    [Theory]
    [InlineData("shell.studio.field.process.steps.assignee.user.help")]
    [InlineData("shell.studio.field.process.steps.escalations.reassign.user.help")]
    [InlineData("shell.studio.field.process.steps.escalations.notify.to.user.help")]
    [InlineData("shell.studio.field.process.steps.escalations.notify.to.kind.user.help")]
    [InlineData("shell.studio.field.process.steps.to.user.help")]
    [InlineData("shell.studio.field.process.steps.to.kind.user.help")]
    public void A_person_a_task_goes_to_is_chosen_by_an_expression(string key) =>
        Assert.StartsWith("Biểu thức chọn người ", ViText(key), StringComparison.Ordinal);

    [Theory]
    [InlineData("shell.studio.field.process.steps.assignee.role.help")]
    [InlineData("shell.studio.field.process.steps.escalations.reassign.role.help")]
    [InlineData("shell.studio.field.scenario.steps.recipient.role.help")]
    public void A_roles_people_are_the_people_who_have_it(string key) =>
        Assert.StartsWith("Những người có vai trò này ", ViText(key), StringComparison.Ordinal);

    [Fact]
    public void A_code_keeps_its_name_with_its_meaning_in_brackets()
    {
        var actions = ViText("shell.studio.field.policy.grants.actions.help");
        Assert.Contains("read (đọc)", actions, StringComparison.Ordinal);
        Assert.Contains("update (cập nhật)", actions, StringComparison.Ordinal);
        Assert.Contains("none (", ViText("shell.studio.field.connector.auth.type.help"), StringComparison.Ordinal);
        Assert.Contains("object (nhóm trường)", ViText("shell.studio.field.connector.operations.input.type.help"), StringComparison.Ordinal);

        var header = ViText("shell.studio.field.connector.auth.name.help");
        Assert.Contains("header", header, StringComparison.Ordinal);
        Assert.DoesNotContain("tiêu đề", header, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shell.studio.field.identityProvider.claims.displayName.help")]
    [InlineData("shell.studio.field.identityProvider.claims.email.help")]
    [InlineData("shell.studio.field.identityProvider.claims.locale.help")]
    [InlineData("shell.studio.field.identityProvider.roles.claim.help")]
    public void An_empty_claim_falls_back_to_a_named_claim(string key) =>
        Assert.Matches(@"Để trống thì dùng claim \S+\.$", ViText(key));

    [Fact]
    public void Axis_is_the_subject_of_what_it_refuses_or_ignores()
    {
        Assert.Contains("Axis từ chối", ViText("shell.studio.field.identityProvider.acr.help"), StringComparison.Ordinal);
        Assert.Contains("Axis bỏ qua", ViText("shell.studio.field.identityProvider.roles.values.help"), StringComparison.Ordinal);
        Assert.Contains("Axis từ chối", ViText("shell.studio.section.entity.validations.line"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_studio_help_says_bi()
    {
        // "bị" as a passive marker only: "thiết bị" (device) and "chuẩn bị" (prepare) are other words.
        var passive = new Regex(@"(?<!\p{L})(?<!thiết )(?<!chuẩn )bị(?!\p{L})", RegexOptions.IgnoreCase);
        var helps = _vi.Keys
            .Where(key => key.StartsWith("shell.studio.", StringComparison.Ordinal)
                && (key.EndsWith(".help", StringComparison.Ordinal) || key.EndsWith(".line", StringComparison.Ordinal)))
            .Append("shell.studio.identityProvider.noAcr");

        var offenders = helps.Where(key => passive.IsMatch(ViText(key))).Order().ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("shell.studio.field.page.parameters.required.help")]
    [InlineData("shell.studio.field.page.widgets.collapsed.help")]
    [InlineData("shell.studio.field.dataSource.parameters.required.help")]
    [InlineData("shell.studio.field.dataSource.search.help")]
    [InlineData("shell.studio.field.seed.sync.help")]
    [InlineData("shell.studio.field.endpoint.input.required.help")]
    [InlineData("shell.studio.field.form.sections.fields.readOnly.help")]
    [InlineData("shell.studio.field.entity.fields.required.help")]
    [InlineData("shell.studio.field.entity.fields.unique.help")]
    [InlineData("shell.studio.field.entity.fields.userId.help")]
    [InlineData("shell.studio.field.entity.fields.searchable.help")]
    [InlineData("shell.studio.field.entity.fields.sensitive.help")]
    [InlineData("shell.studio.field.entity.fields.exactLookup.help")]
    [InlineData("shell.studio.field.entity.indexes.unique.help")]
    public void A_checkbox_says_what_turning_it_on_does_in_Vietnamese(string key) =>
        Assert.StartsWith("Bật để ", ViText(key), StringComparison.Ordinal);

    [Fact]
    public void Used_by_relations_are_the_verb_alone()
    {
        Assert.Equal("Phát ra", ViText("shell.studio.relation.raisedEvent"));
        Assert.Equal("Chờ", ViText("shell.studio.relation.awaitedEvent"));
        Assert.Equal("Gửi đi", ViText("shell.studio.relation.webhookEvent"));
        Assert.Equal("Gọi thao tác", ViText("shell.studio.relation.packageOperation"));
        Assert.Equal("Giả lập thao tác", ViText("shell.studio.relation.stubPackage"));
        Assert.Equal("Kiểm thử", ViText("shell.studio.relation.scenarioProcess"));
        Assert.Equal("Cấp quyền cho vai trò này", ViText("shell.studio.relation.policyRole"));
        Assert.Equal("Kiểm thử với vai trò này", ViText("shell.studio.relation.scenarioRole"));
        Assert.DoesNotContain(
            _vi.Where(pair => pair.Key.StartsWith("shell.studio.relation.", StringComparison.Ordinal)),
            pair => pair.Value.Contains("nút", StringComparison.OrdinalIgnoreCase));
    }

    private string Text(string key) =>
        _en.TryGetValue(key, out var text) ? text : throw new Xunit.Sdk.XunitException($"There is no English text for {key}.");

    private string ViText(string key) =>
        _vi.TryGetValue(key, out var text) ? text : throw new Xunit.Sdk.XunitException($"There is no Vietnamese text for {key}.");
}
