using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Tenant : AggregateRoot<Guid>
{
    public const int DeletionGracePeriodDays = 30;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    public string Name { get; private set; }
    public TenantSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public TenantStatus Status { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? DefaultLanguage { get; private set; }
    public DateTime? ScheduledHardDeleteAt { get; private set; }

    private Tenant(
        Guid id,
        string name,
        TenantSlug slug,
        Email ownerEmail,
        Guid subscriptionPlanId,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        SubscriptionPlanId = subscriptionPlanId;
        Status = TenantStatus.Active;
        CreatedAt = createdAt;
    }

    public static Tenant Create(string name, TenantSlug slug, Email ownerEmail, Guid subscriptionPlanId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        if (subscriptionPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(subscriptionPlanId));

        Tenant Tenant = new Tenant(
            Guid.NewGuid(),
            name.Trim(),
            slug,
            ownerEmail,
            subscriptionPlanId,
            DateTime.UtcNow);
        Tenant.RaiseDomainEvent(new TenantCreated(Tenant.Id, Tenant.Name, slug.Value, ownerEmail.Value));
        return Tenant;
    }

    public static Tenant RegisterForContactVerification(
        string name,
        TenantSlug slug,
        Email contactEmail,
        Guid subscriptionPlanId,
        string termsVersion,
        string privacyVersion)
    {
        Tenant Tenant = Create(name, slug, contactEmail, subscriptionPlanId);
        Tenant.Status = TenantStatus.PendingVerification;
        Tenant.RecordLegalAcceptance(termsVersion, privacyVersion);
        return Tenant;
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
            throw new ArgumentException("Tenant name is required.", nameof(name));

        string trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            throw new ArgumentException(
                $"Tenant name must be between {MinNameLength} and {MaxNameLength} characters.",
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

        if (Status == TenantStatus.DeletionScheduled)
            throw new InvalidOperationException("Tenant deletion is already scheduled.");

        Status = TenantStatus.DeletionScheduled;
        ScheduledHardDeleteAt = utcNow.AddDays(DeletionGracePeriodDays);
    }

    public void CancelScheduledDeletion()
    {
        if (Status != TenantStatus.DeletionScheduled)
            throw new InvalidOperationException("Tenant is not scheduled for deletion.");

        Status = TenantStatus.Active;
        ScheduledHardDeleteAt = null;
    }

    public void MarkDeleted()
    {
        if (Status == TenantStatus.Deleted)
            return;

        Status = TenantStatus.Deleted;
        ScheduledHardDeleteAt = null;
    }

    public void BeginProvisioning()
    {
        if (Status == TenantStatus.Provisioning)
            return;

        if (Status != TenantStatus.Active && Status != TenantStatus.ProvisioningFailed)
            throw new InvalidOperationException(
                "Only active or provisioning-failed Tenants can enter provisioning.");

        Status = TenantStatus.Provisioning;
    }

    public void BeginProvisioningAfterOwnerVerification()
    {
        BeginProvisioningAfterContactVerification();
    }

    public void BeginProvisioningAfterContactVerification()
    {
        if (Status == TenantStatus.PendingVerification)
        {
            Status = TenantStatus.Provisioning;
            RaiseDomainEvent(new TenantVerified(Id));
            return;
        }

        if (Status == TenantStatus.Provisioning)
            return;

        BeginProvisioning();
        RaiseDomainEvent(new TenantVerified(Id));
    }

    public void CompleteProvisioning()
    {
        if (Status != TenantStatus.Provisioning)
            throw new InvalidOperationException("Tenant is not in provisioning state.");

        Status = TenantStatus.Active;
    }

    public void MarkProvisioningFailed()
    {
        if (Status == TenantStatus.ProvisioningFailed)
            return;

        if (Status != TenantStatus.Provisioning)
            throw new InvalidOperationException("Only provisioning Tenants can be marked as failed.");

        Status = TenantStatus.ProvisioningFailed;
    }

    public void RetryProvisioning()
    {
        if (Status != TenantStatus.ProvisioningFailed)
            throw new InvalidOperationException("Only provisioning-failed Tenants can be manually retried.");

        Status = TenantStatus.Provisioning;
        RaiseDomainEvent(new TenantVerified(Id));
    }

    public void Archive()
    {
        if (Status == TenantStatus.Archived)
            throw new InvalidOperationException("Tenant is already archived.");

        Status = TenantStatus.Archived;
    }

    public bool AllowsSignIn() =>
        Status is TenantStatus.Active
            or TenantStatus.Provisioning
            or TenantStatus.ProvisioningFailed
            or TenantStatus.DeletionScheduled;

    /// <summary>
    /// Tenant-scoped module APIs (DataModeling, WorkflowBuilder, etc.) may run only when
    /// the Tenant workspace is ready or in the deletion grace period.
    /// </summary>
    public bool AllowsTenantDataAccess() =>
        Status is TenantStatus.Active or TenantStatus.DeletionScheduled;

    private void EnsureCanManageSettings()
    {
        if (Status == TenantStatus.Deleted)
            throw new InvalidOperationException("Tenant has been deleted.");

        if (Status == TenantStatus.Archived)
            throw new InvalidOperationException("Tenant is archived.");
    }
}
