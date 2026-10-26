using Axis.Configuration.Model;

namespace Axis.Server.Exports;

/// <summary>A column of an export file: the field or measure name in the header row and the type that decides how its cells are written.</summary>
internal sealed record ExportColumn(string Name, FieldType Type);
