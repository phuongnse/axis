using System.Text.RegularExpressions;
using Axis.ControlPlane.Emails;

namespace Axis.ControlPlane.Tests;

public sealed partial class EmailTemplatesTests
{
    private static readonly string[] _timelineKinds = ["trialStarted", "trialEndsIn7Days", "trialEndsTomorrow", "tenantReadOnly", "deletionIn7Days", "tenantDeleted"];

    [Fact]
    public void Every_shipped_kind_has_an_en_and_a_vi_text_that_render()
    {
        var templates = EmailTemplates.LoadEmbedded();

        Assert.NotEmpty(templates.Kinds);
        foreach (var kind in templates.Kinds)
        {
            var values = templates.Placeholders(kind).ToDictionary(name => name, name => $"<{name}>");
            foreach (var language in new[] { "en", "vi" })
            {
                var (subject, body) = templates.Render(kind, language, values);
                Assert.False(string.IsNullOrWhiteSpace(subject), $"{kind} {language} has no subject.");
                Assert.False(string.IsNullOrWhiteSpace(body), $"{kind} {language} has no body.");
                Assert.DoesNotContain("{{", subject + body, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_shipped_set_has_the_kinds_of_the_sign_up_flow_and_the_texts_differ_by_language()
    {
        var templates = EmailTemplates.LoadEmbedded();
        Assert.Contains("tenantReady", templates.Kinds);
        Assert.Contains("provisioningDelayed", templates.Kinds);
        var values = templates.Placeholders("tenantReady").ToDictionary(name => name, name => name);

        Assert.NotEqual(templates.Render("tenantReady", "en", values).Subject, templates.Render("tenantReady", "vi", values).Subject);
    }

    [Fact]
    public void The_english_tenant_ready_email_offers_Axis_support_as_the_only_help()
    {
        var templates = EmailTemplates.LoadEmbedded();
        var values = templates.Placeholders("tenantReady").ToDictionary(name => name, name => $"<{name}>");

        var (_, body) = templates.Render("tenantReady", "en", values);

        Assert.Contains("If you need help, contact Axis support.", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tenantReady", "en", "Your Axis trial is ready")]
    [InlineData("tenantReady", "vi", "Bản dùng thử Axis đã sẵn sàng")]
    [InlineData("provisioningDelayed", "en", "Your Axis trial needs more time to set up")]
    [InlineData("provisioningDelayed", "vi", "Bản dùng thử Axis cần thêm thời gian để chuẩn bị")]
    public void The_provisioning_emails_name_the_trial_in_their_subject(string kind, string language, string subject)
    {
        var templates = EmailTemplates.LoadEmbedded();
        var values = templates.Placeholders(kind).ToDictionary(name => name, name => $"<{name}>");

        var (renderedSubject, _) = templates.Render(kind, language, values);

        Assert.Equal(subject, renderedSubject);
    }

    [Theory]
    [InlineData("en", "The trial is ready. Sign in at:")]
    [InlineData("vi", "Bản dùng thử đã sẵn sàng. Đăng nhập tại:")]
    public void The_tenant_ready_email_says_the_trial_is_ready_and_where_to_sign_in(string language, string sentence)
    {
        var templates = EmailTemplates.LoadEmbedded();
        var values = templates.Placeholders("tenantReady").ToDictionary(name => name, name => $"<{name}>");

        var (_, body) = templates.Render("tenantReady", language, values);

        Assert.Contains(sentence, body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_kind_without_a_vi_text_is_rejected_and_the_error_names_it()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EmailTemplates.Parse([("x.en.txt", Text("Hello"))]));

        Assert.Contains("x", exception.Message, StringComparison.Ordinal);
        Assert.Contains("vi", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_placeholders_in_the_two_languages_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EmailTemplates.Parse(
        [
            ("Axis.ControlPlane.Emails.Templates.welcome.en.txt", Text("Hello {{name}}")),
            ("Axis.ControlPlane.Emails.Templates.welcome.vi.txt", Text("Chào {{fullName}}")),
        ]));

        Assert.Contains("welcome", exception.Message, StringComparison.Ordinal);
        Assert.Contains("placeholders", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_other_than_en_and_vi_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EmailTemplates.Parse(
        [
            ("y.en.txt", Text("Hello")),
            ("y.vi.txt", Text("Chào")),
            ("y.fr.txt", Text("Bonjour")),
        ]));

        Assert.Contains("y", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fr", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_fills_in_the_placeholders_and_rejects_a_missing_value()
    {
        var templates = EmailTemplates.Parse([("z.en.txt", Text("Hello {{name}}")), ("z.vi.txt", Text("Chào {{name}}"))]);

        var (subject, body) = templates.Render("z", "vi", new Dictionary<string, string> { ["name"] = "An {{name}}" });

        Assert.Equal("Subject Chào An {{name}}", subject);
        Assert.Equal("Chào An {{name}}", body);
        Assert.Throws<ArgumentException>(() => templates.Render("z", "vi", new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => templates.Render("z", "fr", new Dictionary<string, string> { ["name"] = "An" }));
        Assert.Throws<ArgumentException>(() => templates.Render("nope", "en", new Dictionary<string, string> { ["name"] = "An" }));
    }

    [Fact]
    public void The_six_timeline_emails_hold_no_secret_or_payment_field_and_link_only_to_the_billing_portal_and_differ_by_language()
    {
        var templates = EmailTemplates.LoadEmbedded();

        foreach (var kind in _timelineKinds)
        {
            Assert.Contains(kind, templates.Kinds);
            var placeholders = templates.Placeholders(kind);
            Assert.All(placeholders, name => Assert.False(SecretOrPayment().IsMatch(name), $"{kind} has the placeholder {name}."));

            // A link is a placeholder that holds the whole address, so the only one allowed is the billing portal.
            Assert.All(placeholders.Where(name => name.EndsWith("Url", StringComparison.Ordinal)), name => Assert.Equal("billingPortalUrl", name));

            var values = placeholders.ToDictionary(name => name, name => $"<{name}>");
            foreach (var language in EmailTemplates.Languages)
            {
                var (subject, body) = templates.Render(kind, language, values);
                var text = subject + "\n" + body;
                Assert.DoesNotContain("http", text.Replace("<billingPortalUrl>", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("!", text, StringComparison.Ordinal);
            }

            Assert.NotEqual(templates.Render(kind, "en", values).Subject, templates.Render(kind, "vi", values).Subject);
        }
    }

    [Fact]
    public void The_timeline_emails_that_name_a_next_step_hold_its_date_and_the_billing_portal_and_the_deletion_email_holds_neither()
    {
        var templates = EmailTemplates.LoadEmbedded();

        foreach (var kind in _timelineKinds.Where(kind => kind != "tenantDeleted"))
        {
            Assert.Equal(["billingPortalUrl", "name", "nextStepAt", "tenantAddress"], templates.Placeholders(kind).Order(StringComparer.Ordinal));
        }

        Assert.Equal(["name", "tenantAddress"], templates.Placeholders("tenantDeleted").Order(StringComparer.Ordinal));
    }

    [GeneratedRegex("password|token|secret|otp|card|bank|iban|account|payment|price|amount", RegexOptions.IgnoreCase)]
    private static partial Regex SecretOrPayment();

    private static string Text(string body) => $"Subject: Subject {body}\r\n\r\n{body}\r\n";
}
