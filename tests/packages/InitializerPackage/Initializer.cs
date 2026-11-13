using System.Runtime.CompilerServices;

namespace InitializerPackage;

/// <summary>Writes <c>module-initializer.marker</c> beside the assembly when the assembly is loaded to run.</summary>
internal static class Initializer
{
#pragma warning disable CA2255 // The test package must run code on load, to show the server never loads it to run
    [ModuleInitializer]
    internal static void Run() =>
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(Initializer).Assembly.Location)!, "module-initializer.marker"), "");
#pragma warning restore CA2255
}
