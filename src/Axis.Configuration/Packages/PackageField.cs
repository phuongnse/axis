using Axis.Configuration.Model;

namespace Axis.Configuration.Packages;

/// <summary>A typed input or output field of a package operation.</summary>
public sealed record PackageField(string Name, FieldType Type, bool Required);
