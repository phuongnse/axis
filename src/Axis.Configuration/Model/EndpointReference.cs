namespace Axis.Configuration.Model;

/// <summary>An endpoint that a grant covers, by id and name rather than by model object.</summary>
public sealed record EndpointReference(Guid Id, string Name);
