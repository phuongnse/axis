namespace Axis.Identity.Application.Repositories;

public interface ICreateOrganizationIdempotencyRepository
{
    Task<CreateOrganizationIdempotencyRecord?> GetAsync(
        Guid userId,
        string key,
        CancellationToken ct = default);

    Task AddAsync(
        Guid userId,
        CreateOrganizationIdempotencyRecord record,
        CancellationToken ct = default);
}

public sealed record CreateOrganizationIdempotencyRecord(string Key, string CanonicalRequest, Guid OrganizationId, Guid WorkspaceId);
