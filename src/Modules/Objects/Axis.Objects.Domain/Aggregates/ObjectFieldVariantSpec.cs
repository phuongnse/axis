namespace Axis.Objects.Domain.Aggregates;

public sealed record ObjectFieldVariantSpec(
    ObjectFieldVariantKind Kind,
    decimal? MinNumber = null,
    decimal? MaxNumber = null,
    DateOnly? MinDate = null,
    DateOnly? MaxDate = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    IReadOnlyList<string>? Options = null);
