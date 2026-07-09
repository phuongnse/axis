using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectDefinitionVersionFieldVariant : Entity<ObjectFieldVariantId>
{
    public ObjectFieldVariantKind Kind { get; private set; }
    public int Order { get; private set; }
    public decimal? MinNumber { get; private set; }
    public decimal? MaxNumber { get; private set; }
    public DateOnly? MinDate { get; private set; }
    public DateOnly? MaxDate { get; private set; }
    public int? MinLength { get; private set; }
    public int? MaxLength { get; private set; }
    public string? Pattern { get; private set; }
    public string[] Options { get; private set; }

    private ObjectDefinitionVersionFieldVariant(
        ObjectFieldVariantId id,
        ObjectFieldVariantKind kind,
        int order,
        decimal? minNumber,
        decimal? maxNumber,
        DateOnly? minDate,
        DateOnly? maxDate,
        int? minLength,
        int? maxLength,
        string? pattern)
        : this(
            id,
            kind,
            order,
            minNumber,
            maxNumber,
            minDate,
            maxDate,
            minLength,
            maxLength,
            pattern,
            [])
    {
    }

    private ObjectDefinitionVersionFieldVariant(
        ObjectFieldVariantId id,
        ObjectFieldVariantKind kind,
        int order,
        decimal? minNumber,
        decimal? maxNumber,
        DateOnly? minDate,
        DateOnly? maxDate,
        int? minLength,
        int? maxLength,
        string? pattern,
        string[] options)
        : base(id)
    {
        Kind = kind;
        Order = order;
        MinNumber = minNumber;
        MaxNumber = maxNumber;
        MinDate = minDate;
        MaxDate = maxDate;
        MinLength = minLength;
        MaxLength = maxLength;
        Pattern = pattern;
        Options = options;
    }

    public static ObjectDefinitionVersionFieldVariant FromCurrentVariant(ObjectFieldVariant variant) =>
        new(
            ObjectFieldVariantId.New(),
            variant.Kind,
            variant.Order,
            variant.MinNumber,
            variant.MaxNumber,
            variant.MinDate,
            variant.MaxDate,
            variant.MinLength,
            variant.MaxLength,
            variant.Pattern,
            variant.Options.ToArray());
}
