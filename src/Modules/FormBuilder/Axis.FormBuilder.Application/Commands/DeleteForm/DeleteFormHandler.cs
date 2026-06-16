using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.DeleteForm;

/// <summary>Guards against referenced forms; soft-deletes if safe.</summary>
public sealed class DeleteFormHandler(
    IFormRepository formRepo,
    IFormDeletionGuard formDeletionGuard,
    IUnitOfWork uow)
    : ICommandHandler<DeleteFormCommand>
{
    public async Task<Result> Handle(DeleteFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.workspaceId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        Result guardResult = await formDeletionGuard.ValidateCanDeleteAsync(
            command.FormId, cancellationToken);
        if (guardResult.IsFailure)
            return guardResult;

        form.Delete();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
