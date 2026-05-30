using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IExternalRegistrationSessionRepository
{
    Task AddAsync(ExternalRegistrationSession session, CancellationToken ct = default);
    Task<ExternalRegistrationSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
