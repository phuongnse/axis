using Axis.Identity.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Role : AggregateRoot<Guid>
{
    private readonly List<string> _permissions = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public Guid TeamAccountId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<string> Permissions => _permissions.AsReadOnly();

    private Role() : base(default) { Name = null!; } // EF Core materialisation

    private Role(Guid id, string name, string? description, bool isSystem,
        Guid teamAccountId, IEnumerable<string> permissions, DateTime createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
        TeamAccountId = teamAccountId;
        CreatedAt = createdAt;
        _permissions.AddRange(permissions.Distinct());
    }

    /// <summary>Creates a custom role. Requires at least one permission.</summary>
    public static Role Create(string name, string? description, Guid teamAccountId,
        IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));

        List<string> permList = permissions.Distinct().ToList();
        if (permList.Count == 0)
            throw new ArgumentException("A role must have at least one permission.", nameof(permissions));

        Role role = new Role(Guid.NewGuid(), name.Trim(), description?.Trim(), false,
            teamAccountId, permList, DateTime.UtcNow);

        role.RaiseDomainEvent(new RoleCreated(role.Id, teamAccountId, role.Name, false));
        return role;
    }

    /// <summary>Creates a system role (Admin, Editor, Viewer, EndUser). Cannot be updated.</summary>
    public static Role CreateSystem(string name, Guid teamAccountId, IEnumerable<string> permissions)
    {
        Role role = new Role(Guid.NewGuid(), name, null, true,
            teamAccountId, permissions, DateTime.UtcNow);

        role.RaiseDomainEvent(new RoleCreated(role.Id, teamAccountId, name, true));
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

        RaiseDomainEvent(new RoleUpdated(Id, TeamAccountId, Name));
    }

    public bool HasPermission(string permission) =>
        _permissions.Contains(permission);

    /// <summary>Adds catalog permissions missing from the system Admin role ( permission backfill).</summary>
    public bool GrantMissingPermissions(IEnumerable<string> permissions)
    {
        if (!IsSystem || !string.Equals(Name, "Admin", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the system Admin role supports permission backfill.");

        if (permissions is null)
            throw new ArgumentNullException(nameof(permissions));

        bool changed = false;
        foreach (string permission in permissions.Distinct())
        {
            if (_permissions.Contains(permission))
                continue;

            _permissions.Add(permission);
            changed = true;
        }

        return changed;
    }
}
