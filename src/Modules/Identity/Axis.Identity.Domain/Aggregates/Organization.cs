using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Organization : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public OrganizationSlug Slug { get; private set; }
    public Email OwnerEmail { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public Guid SubscriptionPlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }

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

    public void ChangeSubscriptionPlan(Guid newPlanId)
    {
        if (newPlanId == Guid.Empty)
            throw new ArgumentException("Subscription plan is required.", nameof(newPlanId));

        SubscriptionPlanId = newPlanId;
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

    public void Archive()
    {
        if (Status == OrganizationStatus.Archived)
            throw new InvalidOperationException("Organization is already archived.");

        Status = OrganizationStatus.Archived;
    }
}
