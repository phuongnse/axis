using Axis.Server.Tests;

// Starts the server in memory with the server unit tests' settings and writes its OpenAPI document, see docs/development.md#build.
// The entry point is not a top-level program, because its generated Program class would hide the server's.
internal static class DocumentWriter
{
    private static async Task Main(string[] args)
    {
        var path = Path.GetFullPath(args.Length == 1 ? args[0] : OpenApiHost.DocumentPath);

        // The test host reads MvcTestingAppManifest.json from the current folder to find the server's content root.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        await using var factory = OpenApiHost.Create();
        await OpenApiHost.WriteAsync(factory, path, CancellationToken.None);
        Console.WriteLine($"Wrote {path}");
    }
}
