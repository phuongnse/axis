using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ScheduleTenantDeletion;

public sealed record ScheduleTenantDeletionCommand(
    Guid tenantId,
    Guid RequestedByUserId,
    string ConfirmationName) : ICommand;
