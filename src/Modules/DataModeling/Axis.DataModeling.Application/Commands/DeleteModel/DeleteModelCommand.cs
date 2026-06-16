using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.DeleteModel;

/// <summary>Soft-delete a data model and all its records.</summary>
public sealed record DeleteModelCommand(Guid ModelId, Guid TeamAccountId) : ICommand;
