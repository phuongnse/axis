namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed record BusinessObjectRecordRuleDiagnostic(
    string NodeId,
    bool IsMatch);

public sealed record BusinessObjectRecordRuleEvaluation(
    string FieldKey,
    Guid BindingId,
    int BindingRevision,
    string DefinitionKey,
    int DefinitionVersion,
    bool IsMatch,
    IReadOnlyList<BusinessObjectRecordRuleDiagnostic> Diagnostics);
