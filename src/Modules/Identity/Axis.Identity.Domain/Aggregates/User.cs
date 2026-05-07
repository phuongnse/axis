using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class User : AggregateRoot<Guid>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly List<Guid> _roleIds = [];

    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string FullName => $"{FirstName} {LastName}";
    public Email Email { get; private set; }
    public Guid OrganizationId { get; private set; }
    public UserStatus Status { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsLockedOut => LockedUntil.HasValue && DateTime.UtcNow < LockedUntil.Value;

    public IReadOnlyList<Guid> RoleIds => _roleIds.AsReadOnly();

    private User(
        Guid id,
        string firstName,
        string lastName,
        Email email,
        Guid organizationId,
        DateTime createdAt)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        OrganizationId = organizationId;
        Status = UserStatus.Active;
        IsEmailVerified = false;
        CreatedAt = createdAt;
    }

    public static User Create(string firstName, string lastName, Email email, Guid organizationId)
    {
        var user = new User(Guid.NewGuid(), firstName, lastName, email, organizationId, DateTime.UtcNow);
        user.RaiseDomainEvent(new UserRegistered(user.Id, organizationId, email.Value));
        return user;
    }

    public void VerifyEmail()
    {
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

    public void AssignRole(Guid roleId)
    {
        if (!_roleIds.Contains(roleId))
            _roleIds.Add(roleId);
    }

    public void RemoveRole(Guid roleId) =>
        _roleIds.Remove(roleId);
}
