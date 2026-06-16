using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Organization : AggregateRoot<Guid>
{
    public const int DeletionGracePeriodDays = 30;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    public string Name { get; private set; }
    public OrganizationSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? DefaultLanguage { get; private set; }
    public DateTime? ScheduledHardDeleteAt { get; private set; }

    private Organization(
        Guid id,
        string name,
        OrganizationSlug slug,
        Email ownerEmail,
        Guid subscriptionPlanId,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        SubscriptionPlanId = subscriptionPlanId;
        Status = OrganizationStatus.Active;
        CreatedAt = createdAt;
    }

    public static Organization Create(string name, OrganizationSlug slug, Email ownerEmail, Guid subscriptionPlanId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));
        if (subscriptionPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(subscriptionPlanId));

        Organization org = new Organization(
            Guid.NewGuid(),
            name.Trim(),
            slug,
            ownerEmail,
            subscriptionPlanId,
            DateTime.UtcNow);
        org.RaiseDomainEvent(new OrganizationCreated(org.Id, org.Name, slug.Value, ownerEmail.Value));
        return org;
    }

    public static Organization RegisterForContactVerification(
        string name,
        OrganizationSlug slug,
        Email contactEmail,
        Guid subscriptionPlanId,
        string termsVersion,
        string privacyVersion)
    {
        Organization org = Create(name, slug, contactEmail, subscriptionPlanId);
        org.Status = OrganizationStatus.PendingVerification;
        org.RecordLegalAcceptance(termsVersion, privacyVersion);
        return org;
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
            throw new ArgumentException("Organization name is required.", nameof(name));

        string trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            throw new ArgumentException(
                $"Organization name must be between {MinNameLength} and {MaxNameLength} characters.",
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

        if (Status == OrganizationStatus.DeletionScheduled)
            throw new InvalidOperationException("Organization deletion is already scheduled.");

        Status = OrganizationStatus.DeletionScheduled;
        ScheduledHardDeleteAt = utcNow.AddDays(DeletionGracePeriodDays);
    }

    public void CancelScheduledDeletion()
    {
        if (Status != OrganizationStatus.DeletionScheduled)
            throw new InvalidOperationException("Organization is not scheduled for deletion.");

        Status = OrganizationStatus.Active;
        ScheduledHardDeleteAt = null;
    }

    public void MarkDeleted()
    {
        if (Status == OrganizationStatus.Deleted)
            return;

        Status = OrganizationStatus.Deleted;
        ScheduledHardDeleteAt = null;
    }

    public void BeginProvisioning()
    {
        if (Status == OrganizationStatus.Provisioning)
            return;

        if (Status != OrganizationStatus.Active && Status != OrganizationStatus.ProvisioningFailed)
            throw new InvalidOperationException(
                "Only active or provisioning-failed organizations can enter provisioning.");

        Status = OrganizationStatus.Provisioning;
    }

    public void BeginProvisioningAfterOwnerVerification()
    {
        BeginProvisioningAfterContactVerification();
    }

    public void BeginProvisioningAfterContactVerification()
    {
        if (Status == OrganizationStatus.PendingVerification)
        {
            Status = OrganizationStatus.Provisioning;
            RaiseDomainEvent(new OrganizationVerified(Id));
            return;
        }

        if (Status == OrganizationStatus.Provisioning)
            return;

        BeginProvisioning();
        RaiseDomainEvent(new OrganizationVerified(Id));
    }

    public void CompleteProvisioning()
    {
        if (Status != OrganizationStatus.Provisioning)
            throw new InvalidOperationException("Organization is not in provisioning state.");

        Status = OrganizationStatus.Active;
    }

    public void MarkProvisioningFailed()
    {
        if (Status == OrganizationStatus.ProvisioningFailed)
            return;

        if (Status != OrganizationStatus.Provisioning)
            throw new InvalidOperationException("Only provisioning organizations can be marked as failed.");

        Status = OrganizationStatus.ProvisioningFailed;
    }

    public void RetryProvisioning()
    {
        if (Status != OrganizationStatus.ProvisioningFailed)
            throw new InvalidOperationException("Only provisioning-failed organizations can be manually retried.");

        Status = OrganizationStatus.Provisioning;
        RaiseDomainEvent(new OrganizationVerified(Id));
    }

    public void Archive()
    {
        if (Status == OrganizationStatus.Archived)
            throw new InvalidOperationException("Organization is already archived.");

        Status = OrganizationStatus.Archived;
    }

    public bool AllowsSignIn() =>
        Status is OrganizationStatus.Active
            or OrganizationStatus.Provisioning
            or OrganizationStatus.ProvisioningFailed
            or OrganizationStatus.DeletionScheduled;

    /// <summary>
    /// Tenant-scoped module APIs (DataModeling, WorkflowBuilder, etc.) may run only when
    /// the org workspace is ready or in the deletion grace period.
    /// </summary>
    public bool AllowsTenantDataAccess() =>
        Status is OrganizationStatus.Active or OrganizationStatus.DeletionScheduled;

    private void EnsureCanManageSettings()
    {
        if (Status == OrganizationStatus.Deleted)
            throw new InvalidOperationException("Organization has been deleted.");

        if (Status == OrganizationStatus.Archived)
            throw new InvalidOperationException("Organization is archived.");
    }
}
