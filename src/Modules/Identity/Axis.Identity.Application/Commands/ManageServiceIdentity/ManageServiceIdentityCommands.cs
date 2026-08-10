using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ManageServiceIdentity;

public sealed record CreateServiceIdentityCommand(Guid ActorUserId, Guid WorkspaceId, string ClientId, string CorrelationId) : ICommand<ServiceIdentityDto>;
public sealed record AddServiceIdentityKeyCommand(Guid ActorUserId, Guid WorkspaceId, Guid ServiceIdentityId, int ExpectedRevision, string PublicJwk, string CorrelationId) : ICommand<ServiceIdentityDto>;
public sealed record RevokeServiceIdentityKeyCommand(Guid ActorUserId, Guid WorkspaceId, Guid ServiceIdentityId, Guid KeyId, int ExpectedRevision, string CorrelationId) : ICommand<ServiceIdentityDto>;
public sealed record RevokeServiceIdentityCommand(Guid ActorUserId, Guid WorkspaceId, Guid ServiceIdentityId, int ExpectedRevision, string CorrelationId) : ICommand<ServiceIdentityDto>;
