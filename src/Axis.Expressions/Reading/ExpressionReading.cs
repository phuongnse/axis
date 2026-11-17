using System.Text.Json.Serialization;

namespace Axis.Expressions.Reading;

/// <summary>
/// A node of the reading of an expression, the tree Studio says in words, see
/// docs/reference/authoring.md#expression-check. Each node's own <see cref="Kind"/> is the <c>kind</c> in JSON, so the
/// derived types carry no separate discriminator.
/// </summary>
[JsonDerivedType(typeof(ReadingGroup))]
[JsonDerivedType(typeof(ReadingPhrase))]
[JsonDerivedType(typeof(ReadingName))]
[JsonDerivedType(typeof(ReadingValue))]
public abstract record ReadingNode(string Kind);

/// <summary>A run of <c>and</c> or <c>or</c>, or a <c>not</c> before one: <c>all</c>, <c>any</c>, <c>notAll</c> or <c>none</c>.</summary>
public sealed record ReadingGroup(string Kind, IReadOnlyList<ReadingNode> Items) : ReadingNode(Kind);

/// <summary>
/// A phrase said with the text <c>shell.studio.expression.reads.{Words}</c>. Each slot is a list of parts, which fills
/// <c>{0}</c>, <c>{1}</c> and so on in that text.
/// </summary>
public sealed record ReadingPhrase(string Words, IReadOnlyList<IReadOnlyList<ReadingNode>> Slots) : ReadingNode("phrase");

/// <summary>A field, a path step or a rule, with the text key of its label, or null when it has none.</summary>
public sealed record ReadingName(string Name, string? LabelKey) : ReadingNode("name");

/// <summary>
/// A literal. <see cref="Type"/> is a type of the Types table in docs/reference/expressions.md, <c>enum</c> for a text
/// compared with an enum. A date is its ISO 8601 text. <see cref="LabelKey"/> is the label key of an enum value, or null.
/// </summary>
public sealed record ReadingValue(string Type, object? Value, string? LabelKey) : ReadingNode("value");
