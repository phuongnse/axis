using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CancelTenantDeletion;

public sealed record CancelTenantDeletionCommand(Guid tenantId) : ICommand;
