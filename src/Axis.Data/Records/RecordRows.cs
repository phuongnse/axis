using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// The rows a body sends for one child collection of <see cref="Collection"/>. Each row holds every
/// column field of <see cref="Child"/> in its declaration order, with a <see langword="null"/>
/// value for a field the row leaves out.
/// </summary>
public sealed record RecordRows(FieldModel Collection, EntityModel Child, IReadOnlyList<IReadOnlyList<RecordValue>> Rows);
