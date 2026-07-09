namespace Axis.Objects.Domain.Aggregates;

public enum ObjectFieldVariantKind
{
    Required = 0,
    NumericRange = 1,
    DateRange = 2,
    TextLength = 3,
    TextPattern = 4,
    SingleSelectOptions = 5,
}
