using System.Text.RegularExpressions;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed class ObjectFieldVariant : Entity<ObjectFieldVariantId>
{
    private static readonly TimeSpan PatternValidationTimeout = TimeSpan.FromMilliseconds(250);

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

    private ObjectFieldVariant(
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

    private ObjectFieldVariant(
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

    public static Result<IReadOnlyList<ObjectFieldVariant>> CreateMany(
        ObjectFieldType fieldType,
        IReadOnlyList<ObjectFieldVariantSpec>? specs)
    {
        if (specs is null || specs.Count == 0)
            return Array.Empty<ObjectFieldVariant>();

        HashSet<ObjectFieldVariantKind> seenKinds = [];
        List<ObjectFieldVariant> variants = [];
        for (int index = 0; index < specs.Count; index++)
        {
            ObjectFieldVariantSpec spec = specs[index];
            if (!Enum.IsDefined(spec.Kind))
                return Result.Failure<IReadOnlyList<ObjectFieldVariant>>("Field variant is not supported.");

            if (!seenKinds.Add(spec.Kind))
                return Result.Failure<IReadOnlyList<ObjectFieldVariant>>("Field variants must be unique per field.");

            Result<ObjectFieldVariant> variant = Create(
                ObjectFieldVariantId.New(),
                fieldType,
                spec,
                index);
            if (variant.IsFailure)
                return Result.Failure<IReadOnlyList<ObjectFieldVariant>>(variant.Error);

            variants.Add(variant.Value);
        }

        return variants;
    }

    public static Result<ObjectFieldVariant> Create(
        ObjectFieldVariantId id,
        ObjectFieldType fieldType,
        ObjectFieldVariantSpec spec,
        int order)
    {
        if (!Enum.IsDefined(fieldType))
            return Result.Failure<ObjectFieldVariant>("Field type is not supported.");

        Result compatible = ValidateCompatibility(fieldType, spec.Kind);
        if (compatible.IsFailure)
            return Result.Failure<ObjectFieldVariant>(compatible.Error);

        return spec.Kind switch
        {
            ObjectFieldVariantKind.Required => New(id, spec.Kind, order),
            ObjectFieldVariantKind.NumericRange => CreateNumericRange(id, fieldType, spec, order),
            ObjectFieldVariantKind.DateRange => CreateDateRange(id, spec, order),
            ObjectFieldVariantKind.TextLength => CreateTextLength(id, spec, order),
            ObjectFieldVariantKind.TextPattern => CreateTextPattern(id, spec, order),
            ObjectFieldVariantKind.SingleSelectOptions => CreateSingleSelectOptions(id, spec, order),
            _ => Result.Failure<ObjectFieldVariant>("Field variant is not supported."),
        };
    }

    public ObjectFieldVariant Snapshot() =>
        new(
            ObjectFieldVariantId.New(),
            Kind,
            Order,
            MinNumber,
            MaxNumber,
            MinDate,
            MaxDate,
            MinLength,
            MaxLength,
            Pattern,
            Options.ToArray());

    private static Result ValidateCompatibility(ObjectFieldType fieldType, ObjectFieldVariantKind kind)
    {
        bool compatible = kind switch
        {
            ObjectFieldVariantKind.Required => true,
            ObjectFieldVariantKind.NumericRange =>
                fieldType is ObjectFieldType.Integer or ObjectFieldType.Decimal,
            ObjectFieldVariantKind.DateRange => fieldType == ObjectFieldType.Date,
            ObjectFieldVariantKind.TextLength => fieldType == ObjectFieldType.Text,
            ObjectFieldVariantKind.TextPattern => fieldType == ObjectFieldType.Text,
            ObjectFieldVariantKind.SingleSelectOptions => fieldType == ObjectFieldType.SingleSelect,
            _ => false,
        };

        return compatible
            ? Result.Success()
            : Result.Failure("Field variant is not compatible with the selected field type.");
    }

    private static Result<ObjectFieldVariant> CreateNumericRange(
        ObjectFieldVariantId id,
        ObjectFieldType fieldType,
        ObjectFieldVariantSpec spec,
        int order)
    {
        if (spec.MinNumber is null && spec.MaxNumber is null)
            return Result.Failure<ObjectFieldVariant>("Numeric range requires at least one bound.");

        if (spec.MinNumber > spec.MaxNumber)
            return Result.Failure<ObjectFieldVariant>("Numeric range minimum cannot exceed maximum.");

        if (
            fieldType == ObjectFieldType.Integer &&
            (!IsWholeNumber(spec.MinNumber) || !IsWholeNumber(spec.MaxNumber))
        )
            return Result.Failure<ObjectFieldVariant>(
                "Numeric range bounds for integer fields must be whole numbers.");

        return New(id, spec.Kind, order, minNumber: spec.MinNumber, maxNumber: spec.MaxNumber);
    }

    private static bool IsWholeNumber(decimal? value) =>
        value is null || decimal.Truncate(value.Value) == value.Value;

    private static Result<ObjectFieldVariant> CreateDateRange(
        ObjectFieldVariantId id,
        ObjectFieldVariantSpec spec,
        int order)
    {
        if (spec.MinDate is null && spec.MaxDate is null)
            return Result.Failure<ObjectFieldVariant>("Date range requires at least one bound.");

        if (spec.MinDate > spec.MaxDate)
            return Result.Failure<ObjectFieldVariant>("Date range minimum cannot exceed maximum.");

        return New(id, spec.Kind, order, minDate: spec.MinDate, maxDate: spec.MaxDate);
    }

    private static Result<ObjectFieldVariant> CreateTextLength(
        ObjectFieldVariantId id,
        ObjectFieldVariantSpec spec,
        int order)
    {
        if (spec.MinLength is null && spec.MaxLength is null)
            return Result.Failure<ObjectFieldVariant>("Text length requires at least one bound.");

        if (spec.MinLength < 0 || spec.MaxLength < 0)
            return Result.Failure<ObjectFieldVariant>("Text length bounds cannot be negative.");

        if (spec.MinLength > spec.MaxLength)
            return Result.Failure<ObjectFieldVariant>("Text length minimum cannot exceed maximum.");

        return New(id, spec.Kind, order, minLength: spec.MinLength, maxLength: spec.MaxLength);
    }

    private static Result<ObjectFieldVariant> CreateTextPattern(
        ObjectFieldVariantId id,
        ObjectFieldVariantSpec spec,
        int order)
    {
        string pattern = spec.Pattern?.Trim() ?? string.Empty;
        if (pattern.Length == 0)
            return Result.Failure<ObjectFieldVariant>("Text pattern is required.");

        try
        {
            _ = new Regex(pattern, RegexOptions.None, PatternValidationTimeout);
        }
        catch (ArgumentException)
        {
            return Result.Failure<ObjectFieldVariant>("Text pattern is invalid.");
        }

        return New(id, spec.Kind, order, pattern: pattern);
    }

    private static Result<ObjectFieldVariant> CreateSingleSelectOptions(
        ObjectFieldVariantId id,
        ObjectFieldVariantSpec spec,
        int order)
    {
        string[] options = (spec.Options ?? [])
            .Select(option => option.Trim())
            .ToArray();

        if (options.Length == 0)
            return Result.Failure<ObjectFieldVariant>("Single-select options require at least one value.");

        if (options.Any(string.IsNullOrWhiteSpace))
            return Result.Failure<ObjectFieldVariant>("Single-select option values are required.");

        if (options.Distinct(StringComparer.Ordinal).Count() != options.Length)
            return Result.Failure<ObjectFieldVariant>("Single-select option values must be unique.");

        return New(id, spec.Kind, order, options: options);
    }

    private static ObjectFieldVariant New(
        ObjectFieldVariantId id,
        ObjectFieldVariantKind kind,
        int order,
        decimal? minNumber = null,
        decimal? maxNumber = null,
        DateOnly? minDate = null,
        DateOnly? maxDate = null,
        int? minLength = null,
        int? maxLength = null,
        string? pattern = null,
        string[]? options = null) =>
        new(
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
            options ?? []);
}
