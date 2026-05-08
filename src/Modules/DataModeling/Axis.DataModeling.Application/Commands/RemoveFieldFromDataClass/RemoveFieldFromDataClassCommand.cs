using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;

/// <summary>US-039: Remove a field from a data class.</summary>
public sealed record RemoveFieldFromDataClassCommand(
    Guid DataClassId,
    Guid FieldId,
    Guid OrganizationId) : ICommand;
