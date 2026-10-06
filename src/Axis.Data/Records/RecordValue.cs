using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// A parsed field value, typed as it is bound: <see cref="string"/> for text, enum and decimal (in
/// plain notation), <see cref="long"/>, <see cref="bool"/>, <see cref="DateOnly"/>,
/// <see cref="DateTimeOffset"/> in UTC with offset zero, and <see cref="Guid"/> for reference.
/// A null value is SQL <c>NULL</c>.
/// </summary>
public sealed record RecordValue(FieldModel Field, object? Value);
