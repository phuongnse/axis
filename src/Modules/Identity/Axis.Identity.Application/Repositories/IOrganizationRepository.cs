using Axis.Identity.Domain.Aggregates;
namespace Axis.Identity.Application.Repositories;

public interface IOrganizationRepository { Task AddAsync(Organization organization, CancellationToken ct = default); Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default); }
