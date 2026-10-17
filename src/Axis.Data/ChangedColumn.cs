using System.Text.Json;

namespace Axis.Data;

/// <summary>
/// A column that an applied migration renamed, dropped or converted, or a table that it dropped. Activation
/// stores these in <c>axis.applied_migrations.changed_columns</c>.
/// </summary>
/// <param name="EntityId">The entity whose table the migration changed.</param>
/// <param name="Field">The field of the changed column, or <see langword="null"/> when the migration dropped the table.</param>
public sealed record ChangedColumn(Guid EntityId, string? Field)
{
    /// <summary>The JSON options of the stored array: camel-case member names.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
}
