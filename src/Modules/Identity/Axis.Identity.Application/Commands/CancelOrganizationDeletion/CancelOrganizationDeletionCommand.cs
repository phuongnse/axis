using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CancelOrganizationDeletion;

public sealed record CancelOrganizationDeletionCommand(Guid OrganizationId) : ICommand;
