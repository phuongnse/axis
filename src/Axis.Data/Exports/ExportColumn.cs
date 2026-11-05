using Axis.Configuration.Model;

namespace Axis.Data.Exports;

/// <summary>A column of an export file: the field or measure name in the header row and the type that decides how its cells are written.</summary>
public sealed record ExportColumn(string Name, FieldType Type);
