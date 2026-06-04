using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class User : AggregateRoot<Guid>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string FullName => $"{FirstName} {LastName}";
    public string? AvatarUrl { get; private set; }
    public Email Email { get; private set; }
    public string? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsLockedOut => LockedUntil.HasValue && DateTime.UtcNow < LockedUntil.Value;

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
        User user = new User(Guid.NewGuid(), firstName, lastName, email, DateTime.UtcNow);
        user.RaiseDomainEvent(new UserRegistered(user.Id, email.Value));
        return user;
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

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedAttempts)
            LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
    }

    public void ResetFailedLogins()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>Test helper — simulates lockout expiry by moving LockedUntil to the past.</summary>
    internal void SimulateLockoutExpiry() =>
        LockedUntil = DateTime.UtcNow.AddMinutes(-1);

    public void Deactivate()
    {
        if (Status == UserStatus.Inactive)
            throw new InvalidOperationException("User is already inactive.");

        Status = UserStatus.Inactive;
    }

    public void Reactivate()
    {
        if (Status == UserStatus.Active)
            throw new InvalidOperationException("User is already active.");

        Status = UserStatus.Active;
    }

    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public void UpdateAvatar(string? avatarUrl)
    {
        AvatarUrl = avatarUrl;
    }
}
