using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class User : AggregateRoot<Guid>
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string FullName => $"{FirstName} {LastName}";
    public Email Email { get; private set; }
    public string? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private User(
        Guid id,
        string firstName,
        string lastName,
        Email email,
        DateTime createdAt)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Status = UserStatus.Active;
        IsEmailVerified = false;
        CreatedAt = createdAt;
    }

    public static User Create(string firstName, string lastName, Email email)
    {
        return new User(Guid.NewGuid(), firstName, lastName, email, DateTime.UtcNow);
    }

    public void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Password hash cannot be empty.", nameof(hash));
        PasswordHash = hash;
    }

    public void RecordLegalAcceptance(string termsVersion, string privacyVersion)
    {
        if (string.IsNullOrWhiteSpace(termsVersion))
            throw new ArgumentException("Terms version is required.", nameof(termsVersion));
        if (string.IsNullOrWhiteSpace(privacyVersion))
            throw new ArgumentException("Privacy version is required.", nameof(privacyVersion));

        AcceptedTermsVersion = termsVersion.Trim();
        AcceptedPrivacyVersion = privacyVersion.Trim();
        LegalAcceptedAt = DateTime.UtcNow;
    }

    public void VerifyEmail()
    {
        if (IsEmailVerified)
            return;

        IsEmailVerified = true;
    }

}
