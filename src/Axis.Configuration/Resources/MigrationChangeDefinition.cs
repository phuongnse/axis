namespace Axis.Configuration.Resources;

/// <summary>
/// One change of a migration as written in the file. The schema accepts only the properties of the
/// change's <see cref="Type"/>: <c>renameField</c> takes <c>from</c> and <c>to</c>, <c>dropField</c>
/// takes <c>field</c>, <c>dropEntity</c> takes none, and <c>fill</c> and <c>convert</c> take
/// <c>field</c> and <c>value</c>.
/// </summary>
public sealed record MigrationChangeDefinition
{
    /// <summary>The kind of change: <c>renameField</c>, <c>dropField</c>, <c>dropEntity</c>, <c>fill</c> or <c>convert</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The field name before a <c>renameField</c>.</summary>
    public string? From { get; init; }

    /// <summary>The field name after a <c>renameField</c>.</summary>
    public string? To { get; init; }

    /// <summary>The field a <c>dropField</c>, <c>fill</c> or <c>convert</c> changes.</summary>
    public string? Field { get; init; }

    /// <summary>The expression of a <c>fill</c> or <c>convert</c>, over the record's own fields.</summary>
    public string? Value { get; init; }
}
