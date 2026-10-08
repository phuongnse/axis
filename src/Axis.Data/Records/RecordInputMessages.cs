using System.Globalization;

namespace Axis.Data.Records;

/// <summary>
/// The fixed messages for problems in a record request body. They never contain storage names,
/// exception text or text from the request; the error key already says where the problem is.
/// </summary>
internal static class RecordInputMessages
{
    public const string MalformedBody = "Must be a JSON object.";

    public const string UnknownProperty = "Unknown property.";

    public const string DuplicateProperty = "Duplicate property.";

    public const string Required = "Required.";

    public const string MustBeObject = "Must be an object.";

    public const string MustBeArray = "Must be an array of row objects.";

    public const string Text = "Must be a text value.";

    public const string NullCharacter = "Must not contain the null character.";

    public const string Integer = "Must be an integral number within the signed 64-bit range.";

    public const string Version = "Must be a positive integral number within the signed 64-bit range.";

    public const string Decimal = "Must be a number.";

    public const string Boolean = "Must be true or false.";

    public const string Date = "Must be a date in the form yyyy-MM-dd.";

    public const string DateTime = "Must be an RFC 3339 date-time with an offset and at most 6 fraction digits.";

    public const string Enum = "Must be one of the enum values.";

    public const string Reference = "Must be a UUID.";

    public const string MissingReference = "No record has this id.";

    public const string NotUnique = "Must be unique.";

    public static string MaxLength(int maxLength) =>
        string.Create(CultureInfo.InvariantCulture, $"Must be at most {maxLength} characters.");

    public static string IntegerDigits(long digits) =>
        string.Create(CultureInfo.InvariantCulture, $"Must have at most {digits} integer digits.");

    public static string FractionDigits(long digits) =>
        string.Create(CultureInfo.InvariantCulture, $"Must have at most {digits} fraction digits.");
}
