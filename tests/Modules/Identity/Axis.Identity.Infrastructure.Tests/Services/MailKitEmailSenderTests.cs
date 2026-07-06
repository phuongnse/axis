using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Tests.Services;

public class MailKitEmailSenderTests
{
    [Fact]
    public void BuildVerificationEmail_WhenBaseUrlIsConfigured_IncludesCompleteVerificationContent()
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", "https://localhost:3000/"));

        VerificationEmailContent content = MailKitEmailSender.BuildVerificationEmail(
            configuration,
            "alex@example.com",
            "abc123",
            "en");
        int currentYear = DateTime.UtcNow.Year;

        content.Subject.Should().Be("Verify your email address");
        content.PlainText.Should().Contain("Axis Platform");
        content.PlainText.Should().Contain("Account security");
        content.PlainText.Should().Contain("Verify your email address");
        content.PlainText.Should().Contain("https://localhost:3000/auth/verify?token=abc123");
        content.PlainText.Should().Contain("This link expires in 24 hours.");
        content.PlainText.Should().Contain("Axis Platform account was created using alex@example.com");
        content.PlainText.Should().Contain("Sent to alex@example.com for Axis Platform account security.");
        content.PlainText.Should().Contain($"\u00A9 {currentYear} Axis Platform. All rights reserved.");

        content.Html.Should().Contain("<!doctype html>");
        content.Html.Should().Contain("<html lang=\"en\">");
        content.Html.Should().Contain("data-template=\"axis-transactional-email\"");
        content.Html.Should().Contain("Confirm this email address to finish securing your Axis Platform account");
        content.Html.Should().Contain("Axis Platform");
        content.Html.Should().Contain("Account security");
        content.Html.Should().Contain("src=\"https://localhost:3000/axis-logo.svg\"");
        content.Html.Should().Contain("alt=\"Axis Platform\"");
        content.Html.Should().Contain("font-family:'Geist',Arial,Helvetica,sans-serif");
        content.Html.Should().Contain("padding-left:12px");
        content.Html.Should().Contain("letter-spacing:0.18em");
        content.Html.Should().Contain("font-weight:600");
        content.Html.Should().NotContain("align=\"right\"");
        content.Html.Should().Contain("background:#c75f1e");
        content.Html.Should().Contain("color:#4d8589");
        content.Html.Should().Contain("max-width:640px");
        content.Html.Should().NotContain("#2563eb");
        content.Html.Should().Contain("href=\"https://localhost:3000/auth/verify?token=abc123\"");
        content.Html.Should().Contain(">Verify email</a>");
        content.Html.Should().Contain("Button unavailable?");
        content.Html.Should().Contain("Security note");
        content.Html.Should().Contain("Sent to alex@example.com for Axis Platform account security.");
        content.Html.Should().Contain($"Axis Platform &copy; {currentYear}. All rights reserved.");
    }

    [Fact]
    public void BuildVerificationEmail_WhenLanguageIsVietnamese_UsesVietnameseContent()
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", "https://localhost:3000/"));

        VerificationEmailContent content = MailKitEmailSender.BuildVerificationEmail(
            configuration,
            "alex@example.com",
            "abc123",
            "vi");

        content.Subject.Should().Be("Xác minh email của bạn");
        content.PlainText.Should().Contain("Xác minh email của bạn");
        content.PlainText.Should().Contain("Chào mừng bạn đến với Axis Platform.");
        content.PlainText.Should().Contain("Liên kết này hết hạn sau 24 giờ.");
        content.PlainText.Should().Contain("một tài khoản Axis Platform đã được tạo bằng alex@example.com");
        content.PlainText.Should().Contain("Mọi quyền được bảo lưu.");
        content.Html.Should().Contain("<html lang=\"vi\">");
        content.Html.Should().Contain(WebUtility.HtmlEncode("Xác minh email"));
        content.Html.Should().Contain(WebUtility.HtmlEncode("Lưu ý bảo mật"));
        content.Html.Should().Contain(
            WebUtility.HtmlEncode("Gửi đến alex@example.com cho mục đích bảo mật tài khoản Axis Platform."));
    }

    [Theory]
    [InlineData("vi-VN", "Xác minh email của bạn")]
    [InlineData("fr", "Verify your email address")]
    [InlineData("", "Verify your email address")]
    public void BuildVerificationEmail_WhenLanguageNeedsFallback_UsesNearestSupportedTemplate(
        string language,
        string expectedSubject)
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", "https://localhost:3000/"));

        VerificationEmailContent content = MailKitEmailSender.BuildVerificationEmail(
            configuration,
            "alex@example.com",
            "abc123",
            language);

        content.Subject.Should().Be(expectedSubject);
    }

    [Fact]
    public void BuildVerificationLink_WhenBaseUrlIsConfigured_UsesConfiguredBrowserFacingOrigin()
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", "https://localhost:3000/"));

        string link = MailKitEmailSender.BuildVerificationLink(configuration, "abc123");

        link.Should().Be("https://localhost:3000/auth/verify?token=abc123");
    }

    [Fact]
    public void BuildBrandLogoUrl_WhenBaseUrlIsConfigured_UsesBrowserFacingPublicLogo()
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", "https://localhost:3000/"));

        string logoUrl = MailKitEmailSender.BuildBrandLogoUrl(configuration);

        logoUrl.Should().Be("https://localhost:3000/axis-logo.svg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildVerificationLink_WhenBaseUrlIsMissing_ThrowsConfigurationError(string? baseUrl)
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", baseUrl));

        Action act = () => MailKitEmailSender.BuildVerificationLink(configuration, "abc123");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("App:BaseUrl must be configured for verification email links.");
    }

    [Theory]
    [InlineData("web:3000")]
    [InlineData("ftp://localhost:3000")]
    public void BuildVerificationLink_WhenBaseUrlIsNotHttpUrl_ThrowsConfigurationError(string baseUrl)
    {
        IConfiguration configuration = ConfigurationWith(("App:BaseUrl", baseUrl));

        Action act = () => MailKitEmailSender.BuildVerificationLink(configuration, "abc123");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("App:BaseUrl must be an absolute http or https URL.");
    }

    [Fact]
    public void BuildFromMailbox_WhenSenderIsConfigured_UsesConfiguredIdentity()
    {
        IConfiguration configuration = ConfigurationWith(
            ("Email:FromAddress", "noreply@axis.localhost"),
            ("Email:FromName", "Axis Platform"));

        MimeKit.MailboxAddress from = MailKitEmailSender.BuildFromMailbox(configuration);

        from.Address.Should().Be("noreply@axis.localhost");
        from.Name.Should().Be("Axis Platform");
    }

    [Fact]
    public void BuildFromMailbox_WhenSenderAddressIsMissing_ThrowsConfigurationError()
    {
        IConfiguration configuration = ConfigurationWith(("Email:FromAddress", ""));

        Action act = () => MailKitEmailSender.BuildFromMailbox(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:FromAddress must be configured for outbound email.");
    }

    private static IConfiguration ConfigurationWith(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select((item) => new KeyValuePair<string, string?>(item.Key, item.Value)))
            .Build();
}
