namespace Axis.Configuration.Model;

/// <summary>A named point of a module's contract that the application turns off, with the reason it gave.</summary>
public sealed record RelaxedPointModel(string Point, string Reason);
