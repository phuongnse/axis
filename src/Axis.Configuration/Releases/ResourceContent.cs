namespace Axis.Configuration.Releases;

/// <summary>
/// One resource file of an application folder: its path relative to the folder with <c>/</c>
/// separators, and its content as canonical JSON (RFC 8785).
/// </summary>
public sealed record ResourceContent(string Path, string Content);
