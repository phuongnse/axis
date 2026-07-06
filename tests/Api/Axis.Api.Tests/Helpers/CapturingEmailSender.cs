using System.Collections.Concurrent;
using Axis.Identity.Application.Services;

namespace Axis.Api.Tests.Helpers;

/// <summary>Records outbound email tokens for integration test assertions.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _verificationTokensByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _verificationLanguagesByEmail = new(StringComparer.OrdinalIgnoreCase);

    public string? GetVerificationToken(string email) =>
        _verificationTokensByEmail.TryGetValue(email, out string? token) ? token : null;

    public string? GetVerificationLanguage(string email) =>
        _verificationLanguagesByEmail.TryGetValue(email, out string? language) ? language : null;

    public Task SendVerificationEmailAsync(
        string toEmail,
        string verificationToken,
        string language,
        CancellationToken ct = default)
    {
        _verificationTokensByEmail[toEmail] = verificationToken;
        _verificationLanguagesByEmail[toEmail] = language;
        return Task.CompletedTask;
    }
}
