using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class ExternalRegistrationSession : Entity<Guid>
{
    public const int SessionLifetimeMinutes = 15;

    public ExternalIdentityProvider Provider { get; private set; }
    public string ProviderKey { get; private set; }
    public Email Email { get; private set; }
    public string DisplayName { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public bool IsCompleted => CompletedAt.HasValue;

    private ExternalRegistrationSession(
        Guid id,
        ExternalIdentityProvider provider,
        string providerKey,
        Email email,
        string displayName,
        DateTime expiresAt,
        DateTime createdAt)
        : base(id)
    {
        Provider = provider;
        ProviderKey = providerKey;
        Email = email;
        DisplayName = displayName;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public static ExternalRegistrationSession Create(
        ExternalIdentityProvider provider,
        string providerKey,
        Email email,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("Provider key is required.", nameof(providerKey));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        DateTime now = DateTime.UtcNow;
        return new ExternalRegistrationSession(
            Guid.NewGuid(),
            provider,
            providerKey.Trim(),
            email,
            displayName.Trim(),
            now.AddMinutes(SessionLifetimeMinutes),
            now);
    }

    public void MarkCompleted()
    {
        if (IsCompleted)
            throw new InvalidOperationException("External registration session is already completed.");

        if (IsExpired)
            throw new InvalidOperationException("External registration session has expired.");

        CompletedAt = DateTime.UtcNow;
    }
}
