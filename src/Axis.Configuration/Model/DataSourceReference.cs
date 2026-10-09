namespace Axis.Configuration.Model;

/// <summary>A data source that a widget reads its rows from, by id and name rather than by model object.</summary>
public sealed record DataSourceReference(Guid Id, string Name);
