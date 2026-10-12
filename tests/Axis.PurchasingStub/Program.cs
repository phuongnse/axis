namespace Axis.PurchasingStub;

// Not top-level statements: the web SDK would make that Program public in the global namespace,
// where it would clash with the server's Program in the integration tests.
internal static class Program
{
    public static async Task Main(string[] args) => await PurchasingStubApp.Create(args).RunAsync();
}
