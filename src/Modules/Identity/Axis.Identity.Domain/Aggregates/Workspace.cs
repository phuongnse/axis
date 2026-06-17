using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Workspace : AggregateRoot<Guid>
{
    public const int DeletionGracePeriodDays = 30;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    public string Name { get; private set; }
    public WorkspaceSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AcceptedTermsVersion { get; private set; }
    public string? AcceptedPrivacyVersion { get; private set; }
    public DateTime? LegalAcceptedAt { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? DefaultLanguage { get; private set; }
    public DateTime? ScheduledHardDeleteAt { get; private set; }

    private Workspace(
        Guid id,
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid? ownerUserId,
        WorkspaceType type,
        Guid subscriptionPlanId,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        OwnerUserId = ownerUserId;
        Type = type;
        SubscriptionPlanId = subscriptionPlanId;
        Status = WorkspaceStatus.Active;
        CreatedAt = createdAt;
    }

    public static Workspace Create(string name, WorkspaceSlug slug, Email ownerEmail, Guid subscriptionPlanId)
    {
        return CreateTeam(name, slug, ownerEmail, subscriptionPlanId);
    }

    public static Workspace CreatePersonal(
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid ownerUserId,
        Guid subscriptionPlanId)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("Owner user is required.", nameof(ownerUserId));

        Workspace workspace = CreateWorkspace(
            name,
            slug,
            ownerEmail,
            ownerUserId,
            WorkspaceType.Personal,
            subscriptionPlanId);
        workspace.Status = WorkspaceStatus.PendingVerification;
        return workspace;
    }

    public static Workspace CreateTeam(
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid subscriptionPlanId)
    {
        return CreateWorkspace(
            name,
            slug,
            ownerEmail,
            null,
            WorkspaceType.Team,
            subscriptionPlanId);
    }

    private static Workspace CreateWorkspace(
        string name,
        WorkspaceSlug slug,
        Email ownerEmail,
        Guid? ownerUserId,
        WorkspaceType type,
        Guid subscriptionPlanId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workspace name is required.", nameof(name));
        if (subscriptionPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(subscriptionPlanId));

        Workspace workspace = new Workspace(
            Guid.NewGuid(),
            name.Trim(),
            slug,
            ownerEmail,
            ownerUserId,
            type,
            subscriptionPlanId,
            DateTime.UtcNow);
        workspace.RaiseDomainEvent(new WorkspaceCreated(workspace.Id, workspace.Name, slug.Value, ownerEmail.Value));
        return workspace;
    }

    public static Workspace RegisterTeamForContactVerification(
        string name,
        WorkspaceSlug slug,
        Email contactEmail,
        Guid subscriptionPlanId,
        string termsVersion,
        string privacyVersion)
    {
        Workspace workspace = CreateTeam(name, slug, contactEmail, subscriptionPlanId);
        workspace.Status = WorkspaceStatus.PendingVerification;
        workspace.RecordLegalAcceptance(termsVersion, privacyVersion);
        return workspace;
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
            throw new ArgumentException("Workspace name is required.", nameof(name));

        string trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength)
            throw new ArgumentException(
                $"Workspace name must be between {MinNameLength} and {MaxNameLength} characters.",
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

        if (Status == WorkspaceStatus.DeletionScheduled)
            throw new InvalidOperationException("Workspace deletion is already scheduled.");

        Status = WorkspaceStatus.DeletionScheduled;
        ScheduledHardDeleteAt = utcNow.AddDays(DeletionGracePeriodDays);
    }

    public void CancelScheduledDeletion()
    {
        if (Status != WorkspaceStatus.DeletionScheduled)
            throw new InvalidOperationException("Workspace is not scheduled for deletion.");

        Status = WorkspaceStatus.Active;
        ScheduledHardDeleteAt = null;
    }

    public void MarkDeleted()
    {
        if (Status == WorkspaceStatus.Deleted)
            return;

        Status = WorkspaceStatus.Deleted;
        ScheduledHardDeleteAt = null;
    }

    public void BeginProvisioning()
    {
        if (Status == WorkspaceStatus.Provisioning)
            return;

        if (Status != WorkspaceStatus.Active && Status != WorkspaceStatus.ProvisioningFailed)
            throw new InvalidOperationException(
                "Only active or provisioning-failed Workspaces can enter provisioning.");

        Status = WorkspaceStatus.Provisioning;
    }

    public void BeginProvisioningAfterOwnerVerification()
    {
        BeginProvisioningAfterContactVerification();
    }

    public void BeginProvisioningAfterContactVerification()
    {
        if (Status == WorkspaceStatus.PendingVerification)
        {
            Status = WorkspaceStatus.Provisioning;
            RaiseDomainEvent(new WorkspaceVerified(Id));
            return;
        }

        if (Status == WorkspaceStatus.Provisioning)
            return;

        BeginProvisioning();
        RaiseDomainEvent(new WorkspaceVerified(Id));
    }

    public void CompleteProvisioning()
    {
        if (Status != WorkspaceStatus.Provisioning)
            throw new InvalidOperationException("Workspace is not in provisioning state.");

        Status = WorkspaceStatus.Active;
    }

    public void MarkProvisioningFailed()
    {
        if (Status == WorkspaceStatus.ProvisioningFailed)
            return;

        if (Status != WorkspaceStatus.Provisioning)
            throw new InvalidOperationException("Only provisioning Workspaces can be marked as failed.");

        Status = WorkspaceStatus.ProvisioningFailed;
    }

    public void RetryProvisioning()
    {
        if (Status != WorkspaceStatus.ProvisioningFailed)
            throw new InvalidOperationException("Only provisioning-failed Workspaces can be manually retried.");

        Status = WorkspaceStatus.Provisioning;
        RaiseDomainEvent(new WorkspaceVerified(Id));
    }

    public void Archive()
    {
        if (Status == WorkspaceStatus.Archived)
            throw new InvalidOperationException("Workspace is already archived.");

        Status = WorkspaceStatus.Archived;
    }

    public bool AllowsSignIn() =>
        Status is WorkspaceStatus.Active
            or WorkspaceStatus.Provisioning
            or WorkspaceStatus.ProvisioningFailed
            or WorkspaceStatus.DeletionScheduled;

    /// <summary>
    /// Workspace-scoped module APIs (DataModeling, WorkflowBuilder, etc.) may run only when
    /// the workspace is ready or in the deletion grace period.
    /// </summary>
    public bool AllowsWorkspaceDataAccess() =>
        Status is WorkspaceStatus.Active or WorkspaceStatus.DeletionScheduled;

    private void EnsureCanManageSettings()
    {
        if (Status == WorkspaceStatus.Deleted)
            throw new InvalidOperationException("Workspace has been deleted.");

        if (Status == WorkspaceStatus.Archived)
            throw new InvalidOperationException("Workspace is archived.");
    }
}
