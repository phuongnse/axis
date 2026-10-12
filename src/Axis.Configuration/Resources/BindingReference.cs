namespace Axis.Configuration.Resources;

/// <summary>A reference to a deployment binding by its name, written as <c>{ "binding": "&lt;name&gt;" }</c>.</summary>
public sealed record BindingReference(string Binding);
