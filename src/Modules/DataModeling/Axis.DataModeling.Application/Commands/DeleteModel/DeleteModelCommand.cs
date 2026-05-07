using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.DeleteModel;

/// <summary>US-033: Soft-delete a data model and all its records.</summary>
public sealed record DeleteModelCommand(Guid ModelId, Guid OrganizationId) : ICommand;
