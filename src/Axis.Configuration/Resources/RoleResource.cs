namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>role</c> resource: a named role that users hold, with a label text key. The schema checks
/// the name and requires the label. The tasks that name it are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record RoleResource : Resource;
