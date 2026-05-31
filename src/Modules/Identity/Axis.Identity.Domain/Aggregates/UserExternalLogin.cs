using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class UserExternalLogin : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public ExternalIdentityProvider Provider { get; private set; }
    public string ProviderKey { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private UserExternalLogin(
        Guid id,
        Guid userId,
        ExternalIdentityProvider provider,
        string providerKey,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        Provider = provider;
        ProviderKey = providerKey;
        CreatedAt = createdAt;
    }

    public static UserExternalLogin Create(
        Guid userId,
        ExternalIdentityProvider provider,
        string providerKey)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("Provider key is required.", nameof(providerKey));

        return new UserExternalLogin(
            Guid.NewGuid(),
            userId,
            provider,
            providerKey.Trim(),
            DateTime.UtcNow);
    }
}
