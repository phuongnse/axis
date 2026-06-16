using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.ReorderFormFields;

public sealed record ReorderFormFieldsCommand(
    Guid FormId,
    Guid tenantId,
    IReadOnlyList<Guid> OrderedFieldIds) : ICommand;
