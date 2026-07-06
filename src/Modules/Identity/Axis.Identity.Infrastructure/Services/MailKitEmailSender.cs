using System.Globalization;
using System.Net;
using Axis.Identity.Application.Services;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    private const string BrandName = "Axis Platform";
    private const string VerificationLinkLifetime = "24 hours";
    private const string BrandLogoPath = "axis-logo.svg";
    private const string DefaultLanguage = "en";
    private const string VietnameseLanguage = "vi";
    private const string BackgroundColor = "#f7f9fb";
    private const string CardColor = "#ffffff";
    private const string ForegroundColor = "#1b1f27";
    private const string MutedColor = "#5e6673";
    private const string BorderColor = "#d8e0ea";
    private const string PrimaryColor = "#c75f1e";
    private const string SecondaryColor = "#4d8589";
    private const string AccentColor = "#edf3f7";
    private const string WarningBackgroundColor = "#fff7ed";
    private const string WarningBorderColor = "#f0b35c";
    private const string WarningTextColor = "#7c3f12";
    private static readonly IReadOnlyDictionary<string, VerificationEmailTemplate> VerificationEmailTemplates =
        new Dictionary<string, VerificationEmailTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultLanguage] = new(
                Language: DefaultLanguage,
                Eyebrow: "Account security",
                Subject: "Verify your email address",
                Preheader: $"Confirm this email address to finish securing your {BrandName} account.",
                Heading: "Verify your email address",
                Intro: $"Welcome to {BrandName}. Confirm this email address to finish setting up your account and keep your sign-in protected.",
                HtmlIntro: $"Welcome to {BrandName}. Confirm this email address to finish setting up your account and keep your sign-in protected.",
                Cta: "Verify email",
                FallbackIntro: "Button unavailable? Copy this secure verification link into your browser:",
                Expiry: $"This link expires in {VerificationLinkLifetime}.",
                SecurityTitle: "Security note",
                SecurityBody: $"This message was sent because an {BrandName} account was created using {{0}}. If that was not you, ignore this email; the account cannot be used until the address is verified.",
                Footer: $"Sent to {{0}} for {BrandName} account security.",
                Copyright: "All rights reserved."),
            [VietnameseLanguage] = new(
                Language: VietnameseLanguage,
                Eyebrow: "Bảo mật tài khoản",
                Subject: "Xác minh email của bạn",
                Preheader: $"Xác nhận email này để hoàn tất bảo mật tài khoản {BrandName}.",
                Heading: "Xác minh email của bạn",
                Intro: $"Chào mừng bạn đến với {BrandName}. Xác nhận địa chỉ email này để hoàn tất thiết lập tài khoản và bảo vệ đăng nhập của bạn.",
                HtmlIntro: $"Chào mừng bạn đến với {BrandName}. Xác nhận địa chỉ email này để hoàn tất thiết lập tài khoản và bảo vệ đăng nhập của bạn.",
                Cta: "Xác minh email",
                FallbackIntro: "Nếu nút không hoạt động, hãy sao chép liên kết xác minh bảo mật này vào trình duyệt:",
                Expiry: "Liên kết này hết hạn sau 24 giờ.",
                SecurityTitle: "Lưu ý bảo mật",
                SecurityBody: $"Email này được gửi vì một tài khoản {BrandName} đã được tạo bằng {{0}}. Nếu đó không phải là bạn, hãy bỏ qua email này; tài khoản chưa thể sử dụng cho đến khi địa chỉ được xác minh.",
                Footer: $"Gửi đến {{0}} cho mục đích bảo mật tài khoản {BrandName}.",
                Copyright: "Mọi quyền được bảo lưu."),
        };

    public async Task SendVerificationEmailAsync(
        string toEmail,
        string verificationToken,
        string language,
        CancellationToken ct = default)
    {
        VerificationEmailContent content = BuildVerificationEmail(
            configuration,
            toEmail,
            verificationToken,
            language);
        await SendAsync(toEmail, content, ct);
    }

    internal static string BuildVerificationLink(IConfiguration configuration, string verificationToken) =>
        $"{GetBaseUrl(configuration)}/auth/verify?token={Uri.EscapeDataString(verificationToken)}";

    internal static string BuildBrandLogoUrl(IConfiguration configuration) =>
        $"{GetBaseUrl(configuration)}/{BrandLogoPath}";

    internal static VerificationEmailContent BuildVerificationEmail(
        IConfiguration configuration,
        string toEmail,
        string verificationToken,
        string language)
    {
        VerificationEmailTemplate template = ResolveVerificationEmailTemplate(language);
        string verificationLink = BuildVerificationLink(configuration, verificationToken);
        string brandLogoUrl = BuildBrandLogoUrl(configuration);
        string escapedVerificationLink = WebUtility.HtmlEncode(verificationLink);
        string escapedBrandLogoUrl = WebUtility.HtmlEncode(brandLogoUrl);
        string year = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        string copyrightSymbol = "\u00A9";
        string securityBody = string.Format(CultureInfo.InvariantCulture, template.SecurityBody, toEmail);
        string footer = string.Format(CultureInfo.InvariantCulture, template.Footer, toEmail);
        TransactionalEmail email = new(
            Language: template.Language,
            Subject: template.Subject,
            Preheader: template.Preheader,
            Eyebrow: template.Eyebrow,
            Heading: template.Heading,
            Intro: template.Intro,
            HtmlIntro: template.HtmlIntro,
            ActionLabel: template.Cta,
            ActionUrl: verificationLink,
            FallbackIntro: template.FallbackIntro,
            Expiry: template.Expiry,
            SecurityTitle: template.SecurityTitle,
            SecurityBody: securityBody,
            Footer: footer,
            Copyright: template.Copyright,
            Year: year,
            CopyrightSymbol: copyrightSymbol);

        string plainText = BuildPlainText(email);
        string html = BuildHtml(email, escapedVerificationLink, escapedBrandLogoUrl);

        return new VerificationEmailContent(template.Subject, plainText, html);
    }

    private static string BuildPlainText(TransactionalEmail email) =>
        $"""
        {BrandName}
        {email.Eyebrow}

        {email.Heading}

        {email.Intro}

        {email.ActionLabel}:
        {email.ActionUrl}

        {email.Expiry}

        {email.SecurityBody}

        {email.Footer}
        {email.CopyrightSymbol} {email.Year} {BrandName}. {email.Copyright}
        """;

    private static string BuildHtml(
        TransactionalEmail email,
        string escapedActionUrl,
        string escapedBrandLogoUrl)
    {
        string escapedLanguage = WebUtility.HtmlEncode(email.Language);
        string escapedBrandName = WebUtility.HtmlEncode(BrandName);
        string escapedSubject = WebUtility.HtmlEncode(email.Subject);
        string escapedPreheader = WebUtility.HtmlEncode(email.Preheader);
        string escapedEyebrow = WebUtility.HtmlEncode(email.Eyebrow);
        string escapedHeading = WebUtility.HtmlEncode(email.Heading);
        string escapedHtmlIntro = WebUtility.HtmlEncode(email.HtmlIntro);
        string escapedActionLabel = WebUtility.HtmlEncode(email.ActionLabel);
        string escapedFallbackIntro = WebUtility.HtmlEncode(email.FallbackIntro);
        string escapedExpiry = WebUtility.HtmlEncode(email.Expiry);
        string escapedSecurityTitle = WebUtility.HtmlEncode(email.SecurityTitle);
        string escapedSecurityBody = WebUtility.HtmlEncode(email.SecurityBody);
        string escapedFooter = WebUtility.HtmlEncode(email.Footer);
        string escapedCopyright = WebUtility.HtmlEncode(email.Copyright);

        return $$"""
            <!doctype html>
            <html lang="{{escapedLanguage}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{{escapedSubject}}</title>
            </head>
            <body style="margin:0; padding:0; background:{{BackgroundColor}}; color:{{ForegroundColor}}; font-family:'Geist',Arial,Helvetica,sans-serif;">
              <div style="display:none; max-height:0; overflow:hidden; opacity:0;">{{escapedPreheader}}</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" data-template="axis-transactional-email" style="background:{{BackgroundColor}}; padding:40px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:640px; background:{{CardColor}}; border:1px solid {{BorderColor}}; border-radius:12px; overflow:hidden; box-shadow:0 18px 48px rgba(27,31,39,0.08);">
                      <tr>
                        <td style="height:6px; background:{{PrimaryColor}}; line-height:6px; font-size:0;">&nbsp;</td>
                      </tr>
                      <tr>
                        <td style="padding:26px 32px 18px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td align="left" style="width:44px; vertical-align:middle;">
                                <img src="{{escapedBrandLogoUrl}}" width="40" height="40" alt="{{escapedBrandName}}" style="display:block; border:0; outline:none; text-decoration:none;">
                              </td>
                              <td align="left" style="padding-left:12px; color:{{MutedColor}}; font-family:'Geist',Arial,Helvetica,sans-serif; font-size:12px; font-weight:600; letter-spacing:0.18em; text-transform:uppercase; vertical-align:middle;">{{escapedEyebrow}}</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:18px 32px 34px;">
                          <h1 style="margin:0 0 14px; color:{{ForegroundColor}}; font-size:28px; line-height:1.25; font-weight:700;">{{escapedHeading}}</h1>
                          <p style="margin:0 0 28px; color:{{MutedColor}}; font-size:16px; line-height:1.65;">{{escapedHtmlIntro}}</p>
                          <p style="margin:0 0 28px;">
                            <a href="{{escapedActionUrl}}" style="display:inline-block; border-radius:8px; background:{{PrimaryColor}}; color:#ffffff; font-size:16px; font-weight:700; line-height:1; padding:15px 22px; text-decoration:none;">{{escapedActionLabel}}</a>
                          </p>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid {{BorderColor}}; border-radius:10px; background:{{AccentColor}};">
                            <tr>
                              <td style="padding:16px 18px;">
                                <p style="margin:0 0 8px; color:{{MutedColor}}; font-size:14px; line-height:1.6;">{{escapedFallbackIntro}}</p>
                                <p style="margin:0; color:{{SecondaryColor}}; font-size:14px; line-height:1.6; word-break:break-all;">
                                  <a href="{{escapedActionUrl}}" style="color:{{SecondaryColor}}; text-decoration:underline;">{{escapedActionUrl}}</a>
                                </p>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:18px 0 22px; color:{{MutedColor}}; font-size:14px; line-height:1.6;">{{escapedExpiry}}</p>
                          <div style="border:1px solid {{WarningBorderColor}}; border-radius:10px; background:{{WarningBackgroundColor}}; color:{{WarningTextColor}}; font-size:14px; line-height:1.65; padding:16px 18px;">
                            <strong>{{escapedSecurityTitle}}</strong><br>
                            {{escapedSecurityBody}}
                          </div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:22px 32px; background:{{AccentColor}}; border-top:1px solid {{BorderColor}}; color:{{MutedColor}}; font-size:12px; line-height:1.65;">
                          <p style="margin:0 0 8px;">{{escapedFooter}}</p>
                          <p style="margin:0;">{{escapedBrandName}} &copy; {{email.Year}}. {{escapedCopyright}}</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    internal static MailboxAddress BuildFromMailbox(IConfiguration configuration)
    {
        IConfigurationSection email = configuration.GetSection("Email");
        IConfigurationSection smtp = email.GetSection("Smtp");

        string fromAddress = FirstConfigured(
            email["FromAddress"],
            smtp["FromAddress"],
            smtp["From"],
            email["From"])
            ?? throw new InvalidOperationException("Email:FromAddress must be configured for outbound email.");
        string? fromName = FirstConfigured(email["FromName"], smtp["FromName"]);

        return string.IsNullOrWhiteSpace(fromName)
            ? MailboxAddress.Parse(fromAddress.Trim())
            : new MailboxAddress(fromName.Trim(), fromAddress.Trim());
    }

    private async Task SendAsync(string toEmail, VerificationEmailContent content, CancellationToken ct)
    {
        IConfigurationSection email = configuration.GetSection("Email");
        IConfigurationSection smtp = email.GetSection("Smtp");

        MailboxAddress from = BuildFromMailbox(configuration);
        string host = FirstConfiguredOrDefault("localhost", email["Host"], smtp["Host"]);
        string portValue = FirstConfiguredOrDefault("587", email["Port"], smtp["Port"]);
        string? username = FirstConfigured(email["Username"], smtp["Username"]);
        string password = FirstConfigured(email["Password"], smtp["Password"]) ?? string.Empty;

        MimeMessage message = new MimeMessage();
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = content.Subject;
        message.Body = new Multipart("alternative")
        {
            new TextPart("plain") { Text = content.PlainText },
            new TextPart("html") { Text = content.Html },
        };

        using SmtpClient client = new SmtpClient();
        await client.ConnectAsync(host, int.Parse(portValue), cancellationToken: ct);

        if (username is not null)
            await client.AuthenticateAsync(username, password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private static string? FirstConfigured(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string FirstConfiguredOrDefault(string defaultValue, params string?[] values) =>
        FirstConfigured(values) ?? defaultValue;

    private static string GetBaseUrl(IConfiguration configuration)
    {
        string? configuredBaseUrl = configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
            throw new InvalidOperationException("App:BaseUrl must be configured for verification email links.");

        string trimmedBaseUrl = configuredBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("App:BaseUrl must be an absolute http or https URL.");
        }

        return trimmedBaseUrl;
    }

    private static VerificationEmailTemplate ResolveVerificationEmailTemplate(string? language)
    {
        foreach (string candidate in LocaleCandidates(language))
        {
            if (VerificationEmailTemplates.TryGetValue(candidate, out VerificationEmailTemplate? template))
                return template;
        }

        return VerificationEmailTemplates[DefaultLanguage];
    }

    private static IEnumerable<string> LocaleCandidates(string? language)
    {
        string? normalized = string.IsNullOrWhiteSpace(language)
            ? null
            : language.Trim().Replace('_', '-');
        if (normalized is null)
        {
            yield return DefaultLanguage;
            yield break;
        }

        yield return normalized;

        int separatorIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (separatorIndex > 0)
            yield return normalized[..separatorIndex];

        yield return DefaultLanguage;
    }
}

internal sealed record VerificationEmailTemplate(
    string Language,
    string Eyebrow,
    string Subject,
    string Preheader,
    string Heading,
    string Intro,
    string HtmlIntro,
    string Cta,
    string FallbackIntro,
    string Expiry,
    string SecurityTitle,
    string SecurityBody,
    string Footer,
    string Copyright);

internal sealed record VerificationEmailContent(
    string Subject,
    string PlainText,
    string Html);

internal sealed record TransactionalEmail(
    string Language,
    string Subject,
    string Preheader,
    string Eyebrow,
    string Heading,
    string Intro,
    string HtmlIntro,
    string ActionLabel,
    string ActionUrl,
    string FallbackIntro,
    string Expiry,
    string SecurityTitle,
    string SecurityBody,
    string Footer,
    string Copyright,
    string Year,
    string CopyrightSymbol);
