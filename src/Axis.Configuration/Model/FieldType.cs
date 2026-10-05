using System.Diagnostics.CodeAnalysis;

namespace Axis.Configuration.Model;

/// <summary>The type of a field. Members mirror the type names in entity files.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The names are the field types of the configuration format.")]
public enum FieldType
{
    Text,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Enum,
    Reference,
}
