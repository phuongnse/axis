namespace Axis.Identity.Application.Repositories;

public interface ICreateOrganizationIdempotencyRepository { Task<CreateOrganizationIdempotencyRecord?> GetAsync(string key, CancellationToken ct = default); Task AddAsync(CreateOrganizationIdempotencyRecord record, CancellationToken ct = default); }
public sealed record CreateOrganizationIdempotencyRecord(string Key, string CanonicalRequest, Guid OrganizationId, Guid WorkspaceId);
