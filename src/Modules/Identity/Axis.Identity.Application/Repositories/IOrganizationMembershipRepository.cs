using Axis.Identity.Domain.Aggregates;
namespace Axis.Identity.Application.Repositories;

public interface IOrganizationMembershipRepository { Task AddAsync(OrganizationMembership membership, CancellationToken ct = default); Task<OrganizationMembership?> GetActiveAsync(Guid organizationId, Guid userId, CancellationToken ct = default); }
