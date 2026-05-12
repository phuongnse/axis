using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Role : AggregateRoot<Guid>
{
    private readonly List<string> _permissions = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<string> Permissions => _permissions.AsReadOnly();

    private Role() : base(default) { Name = null!; } // EF Core materialisation

    private Role(Guid id, string name, string? description, bool isSystem,
        Guid organizationId, IEnumerable<string> permissions, DateTime createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
        OrganizationId = organizationId;
        CreatedAt = createdAt;
        _permissions.AddRange(permissions.Distinct());
    }

    /// <summary>Creates a custom role. Requires at least one permission.</summary>
    public static Role Create(string name, string? description, Guid organizationId,
        IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));

        List<string> permList = permissions.Distinct().ToList();
        if (permList.Count == 0)
            throw new ArgumentException("A role must have at least one permission.", nameof(permissions));

        Role role = new Role(Guid.NewGuid(), name.Trim(), description?.Trim(), false,
            organizationId, permList, DateTime.UtcNow);

        role.RaiseDomainEvent(new RoleCreated(role.Id, organizationId, role.Name, false));
        return role;
    }

    /// <summary>Creates a system role (Admin, Editor, Viewer, EndUser). Cannot be updated.</summary>
    public static Role CreateSystem(string name, Guid organizationId, IEnumerable<string> permissions)
    {
        Role role = new Role(Guid.NewGuid(), name, null, true,
            organizationId, permissions, DateTime.UtcNow);

        role.RaiseDomainEvent(new RoleCreated(role.Id, organizationId, name, true));
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsSystem)
            throw new InvalidOperationException("Cannot modify a system role.");

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));

        List<string> permList = permissions.Distinct().ToList();
        if (permList.Count == 0)
            throw new ArgumentException("A role must have at least one permission.", nameof(permissions));

        Name = name.Trim();
        Description = description?.Trim();
        _permissions.Clear();
        _permissions.AddRange(permList);

        RaiseDomainEvent(new RoleUpdated(Id, OrganizationId, Name));
    }

    public bool HasPermission(string permission) =>
        _permissions.Contains(permission);
}
