namespace Axis.Configuration.Model;

/// <summary>The sequence that numbers a text field: its id, its name and the format of each number.</summary>
public sealed record SequenceModel(Guid Id, string Name, string Format);
