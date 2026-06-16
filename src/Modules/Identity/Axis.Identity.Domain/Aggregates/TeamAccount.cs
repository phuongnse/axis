using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class TeamAccount : AggregateRoot<Guid>
{
    public const int DeletionGracePeriodDays = 30;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    public string Name { get; private set; }
    public TeamAccountSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public TeamAccountStatus Status { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? DefaultLanguage { get; private set; }
    public DateTime? ScheduledHardDeleteAt { get; private set; }

    private TeamAccount(
        Guid id,
        string name,
        TeamAccountSlug slug,
        Email ownerEmail,
        Guid subscriptionPlanId,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        SubscriptionPlanId = subscriptionPlanId;
        Status = TeamAccountStatus.Active;
        CreatedAt = createdAt;
    }

    public static TeamAccount Create(string name, TeamAccountSlug slug, Email ownerEmail, Guid subscriptionPlanId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Team account name is required.", nameof(name));
        if (subscriptionPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(subscriptionPlanId));

        TeamAccount teamAccount = new TeamAccount(
            Guid.NewGuid(),
            name.Trim(),
            slug,
            ownerEmail,
            subscriptionPlanId,
            DateTime.UtcNow);
        teamAccount.RaiseDomainEvent(new TeamAccountCreated(teamAccount.Id, teamAccount.Name, slug.Value, ownerEmail.Value));
        return teamAccount;
    }

    public static TeamAccount RegisterForContactVerification(
        string name,
        TeamAccountSlug slug,
        Email contactEmail,
        Guid subscriptionPlanId,
        string termsVersion,
        string privacyVersion)
    {
        TeamAccount teamAccount = Create(name, slug, contactEmail, subscriptionPlanId);
        teamAccount.Status = TeamAccountStatus.PendingVerification;
        teamAccount.RecordLegalAcceptance(termsVersion, privacyVersion);
        return teamAccount;
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

    public void ChangeSubscriptionPlan(Guid newPlanId)
    {
        if (newPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(newPlanId));

        SubscriptionPlanId = newPlanId;
    }

    public void UpdateProfile(string name, string? timeZoneId, string? defaultLanguage)
    {
        EnsureCanManageSettings();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Team account name is required.", nameof(name));

        string trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            throw new ArgumentException(
                $"Team account name must be between {MinNameLength} and {MaxNameLength} characters.",
                nameof(name));

        Name = trimmed;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        DefaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? null : defaultLanguage.Trim();
    }

    public void UpdateLogoUrl(string? logoUrl)
    {
        EnsureCanManageSettings();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
    }

    public void ScheduleDeletion(DateTime utcNow)
    {
        EnsureCanManageSettings();

        if (Status == TeamAccountStatus.DeletionScheduled)
            throw new InvalidOperationException("Team account deletion is already scheduled.");

        Status = TeamAccountStatus.DeletionScheduled;
        ScheduledHardDeleteAt = utcNow.AddDays(DeletionGracePeriodDays);
    }

    public void CancelScheduledDeletion()
    {
        if (Status != TeamAccountStatus.DeletionScheduled)
            throw new InvalidOperationException("Team account is not scheduled for deletion.");

        Status = TeamAccountStatus.Active;
        ScheduledHardDeleteAt = null;
    }

    public void MarkDeleted()
    {
        if (Status == TeamAccountStatus.Deleted)
            return;

        Status = TeamAccountStatus.Deleted;
        ScheduledHardDeleteAt = null;
    }

    public void BeginProvisioning()
    {
        if (Status == TeamAccountStatus.Provisioning)
            return;

        if (Status != TeamAccountStatus.Active && Status != TeamAccountStatus.ProvisioningFailed)
            throw new InvalidOperationException(
                "Only active or provisioning-failed team accounts can enter provisioning.");

        Status = TeamAccountStatus.Provisioning;
    }

    public void BeginProvisioningAfterOwnerVerification()
    {
        BeginProvisioningAfterContactVerification();
    }

    public void BeginProvisioningAfterContactVerification()
    {
        if (Status == TeamAccountStatus.PendingVerification)
        {
            Status = TeamAccountStatus.Provisioning;
            RaiseDomainEvent(new TeamAccountVerified(Id));
            return;
        }

        if (Status == TeamAccountStatus.Provisioning)
            return;

        BeginProvisioning();
        RaiseDomainEvent(new TeamAccountVerified(Id));
    }

    public void CompleteProvisioning()
    {
        if (Status != TeamAccountStatus.Provisioning)
            throw new InvalidOperationException("Team account is not in provisioning state.");

        Status = TeamAccountStatus.Active;
    }

    public void MarkProvisioningFailed()
    {
        if (Status == TeamAccountStatus.ProvisioningFailed)
            return;

        if (Status != TeamAccountStatus.Provisioning)
            throw new InvalidOperationException("Only provisioning team accounts can be marked as failed.");

        Status = TeamAccountStatus.ProvisioningFailed;
    }

    public void RetryProvisioning()
    {
        if (Status != TeamAccountStatus.ProvisioningFailed)
            throw new InvalidOperationException("Only provisioning-failed team accounts can be manually retried.");

        Status = TeamAccountStatus.Provisioning;
        RaiseDomainEvent(new TeamAccountVerified(Id));
    }

    public void Archive()
    {
        if (Status == TeamAccountStatus.Archived)
            throw new InvalidOperationException("Team account is already archived.");

        Status = TeamAccountStatus.Archived;
    }

    public bool AllowsSignIn() =>
        Status is TeamAccountStatus.Active
            or TeamAccountStatus.Provisioning
            or TeamAccountStatus.ProvisioningFailed
            or TeamAccountStatus.DeletionScheduled;

    /// <summary>
    /// Tenant-scoped module APIs (DataModeling, WorkflowBuilder, etc.) may run only when
    /// the team account is ready or in the deletion grace period.
    /// </summary>
    public bool AllowsTenantDataAccess() =>
        Status is TeamAccountStatus.Active or TeamAccountStatus.DeletionScheduled;

    private void EnsureCanManageSettings()
    {
        if (Status == TeamAccountStatus.Deleted)
            throw new InvalidOperationException("Team account has been deleted.");

        if (Status == TeamAccountStatus.Archived)
            throw new InvalidOperationException("Team account is archived.");
    }
}
