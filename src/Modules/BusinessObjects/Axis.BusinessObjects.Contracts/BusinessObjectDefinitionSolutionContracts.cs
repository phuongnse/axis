using Axis.Identity.Contracts;

namespace Axis.BusinessObjects.Contracts;

public enum BusinessObjectSolutionFieldType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    DateTime = 4,
    Boolean = 5,
    Choice = 6,
}

public enum BusinessObjectSolutionChoiceSelectionMode
{
    Single = 0,
    Multiple = 1,
}

public sealed record BusinessObjectSolutionChoiceOption(
    string OptionKey,
    string Label,
    int Order);

public sealed record BusinessObjectSolutionChoiceConfiguration(
    BusinessObjectSolutionChoiceSelectionMode SelectionMode,
    IReadOnlyList<BusinessObjectSolutionChoiceOption> Options);

public sealed record BusinessObjectDefinitionSolutionField(
    string FieldKey,
    string Label,
    int Order,
    BusinessObjectSolutionFieldType FieldType,
    BusinessObjectSolutionChoiceConfiguration? ChoiceConfiguration,
    IReadOnlyList<string> BindingKeys);

public sealed record BusinessObjectDefinitionSolutionComponent(
    string ComponentKey,
    string ObjectKey,
    string Name,
    IReadOnlyList<BusinessObjectDefinitionSolutionField> Fields);

public sealed record BusinessObjectDefinitionInstallationReceipt(
    Guid SolutionVersionId,
    SubjectReference Actor,
    string ComponentHash,
    Guid OperationId,
    Guid StepId,
    long LeaseEpoch);

public sealed record BusinessObjectDefinitionInstallationResult(
    bool IsSuccess,
    string? ProblemCode = null);

public sealed record BusinessObjectDefinitionInstallationReadBack(
    Guid WorkspaceId,
    Guid DefinitionId,
    Guid PublishedVersionId,
    string ComponentKey,
    BusinessObjectDefinitionSolutionComponent Component,
    Guid SolutionVersionId,
    string ComponentHash,
    Guid OperationId,
    Guid StepId,
    long LeaseEpoch);

public interface IBusinessObjectDefinitionSolutionInstaller
{
    Task<BusinessObjectDefinitionInstallationResult> ValidateAsync(
        Guid workspaceId,
        BusinessObjectDefinitionSolutionComponent component,
        CancellationToken cancellationToken = default);

    Task<BusinessObjectDefinitionInstallationResult> InstallAsync(
        Guid workspaceId,
        BusinessObjectDefinitionSolutionComponent component,
        BusinessObjectDefinitionInstallationReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<BusinessObjectDefinitionInstallationReadBack?> ReadBackAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken cancellationToken = default);
}
