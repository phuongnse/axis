using Axis.ControlPlane.Emails;

namespace Axis.ControlPlane.Tests;

public sealed class EmailTemplatesTests
{
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

    private static string Text(string body) => $"Subject: Subject {body}\r\n\r\n{body}\r\n";
}
