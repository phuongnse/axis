using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.DeleteDataClass;

/// <summary>US-040: Soft-delete a data class that is not referenced by any model field.</summary>
public sealed record DeleteDataClassCommand(Guid DataClassId, Guid OrganizationId) : ICommand;
