using System.Text.Json;

namespace Axis.Configuration.Resources;

/// <summary>
/// A seed record as written in the seed file: its fixed id and its <c>values</c> object, in the
/// shape of a record API create body's <c>values</c>.
/// </summary>
public sealed record SeedRecordDefinition(Guid Id, JsonElement Values);
