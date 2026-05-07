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
    public DateTime CreatedAt { get; private set; }

    private Organization(
        Guid id,
        string name,
        OrganizationSlug slug,
        Email ownerEmail,
        DateTime createdAt)
        : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerEmail = ownerEmail;
        Status = OrganizationStatus.Active;
        CreatedAt = createdAt;
    }

    public static Organization Create(string name, OrganizationSlug slug, Email ownerEmail)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));

        var org = new Organization(Guid.NewGuid(), name.Trim(), slug, ownerEmail, DateTime.UtcNow);
        org.RaiseDomainEvent(new OrganizationCreated(org.Id, org.Name, slug.Value, ownerEmail.Value));
        return org;
    }

    public void Archive()
    {
        if (Status == OrganizationStatus.Archived)
            throw new InvalidOperationException("Organization is already archived.");

        Status = OrganizationStatus.Archived;
    }
}
