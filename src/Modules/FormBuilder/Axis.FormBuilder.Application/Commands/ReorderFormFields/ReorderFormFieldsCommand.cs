using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.ReorderFormFields;

public sealed record ReorderFormFieldsCommand(
    Guid FormId,
    Guid TeamAccountId,
    IReadOnlyList<Guid> OrderedFieldIds) : ICommand;
