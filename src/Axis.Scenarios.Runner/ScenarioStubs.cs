using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;
using Axis.Extensions;
using Axis.Processes.Instances;

namespace Axis.Scenarios.Runner;

/// <summary>
/// The stubs of a scenario. They stand in for the HTTP request of a connector call and for the
/// package operation of a package call, so the real call handlers run and no real system or package
/// code does. A stub with an output answers every call of its operation with that output. A stub with
/// <c>outcome: failed</c> makes the call fail at once, with no retry. A connector stub matches the
/// connector and the operation ignoring letter case. A package stub matches the package id exactly
/// and the operation ignoring letter case.
/// </summary>
internal sealed class ScenarioStubs
{
    private readonly Dictionary<(string Connector, string Operation), ScenarioStubDefinition> _connectors = new();
    private readonly Dictionary<(string Package, string Operation), ScenarioStubDefinition> _packages = new();

    public ScenarioStubs(IEnumerable<ScenarioStubDefinition> stubs)
    {
        foreach (var stub in stubs)
        {
            if (stub.Connector is { } reference)
            {
                _connectors.TryAdd((reference.Name.ToUpperInvariant(), reference.Operation.ToUpperInvariant()), stub);
            }
            else
            {
                // The schema requires a connector or an operation written <package>/<operation>.
                var separator = stub.Operation!.IndexOf('/', StringComparison.Ordinal);
                _packages.TryAdd((stub.Operation[..separator], stub.Operation[(separator + 1)..].ToUpperInvariant()), stub);
            }
        }
    }

    /// <summary>The handler that answers the HTTP requests of connector calls from the stubs.</summary>
    public HttpMessageHandler HttpHandler => new StubHandler(this);

    public bool HasConnector(string connector, string operation) =>
        _connectors.ContainsKey((connector.ToUpperInvariant(), operation.ToUpperInvariant()));

    public bool HasPackage(string package, string operation) =>
        _packages.ContainsKey((package, operation.ToUpperInvariant()));

    /// <summary>Creates the operation that answers from its stub in place of the package's own code.</summary>
    public IOperation CreateOperation(InstalledPackage package, string operation)
    {
        var manifest = package.Manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, operation, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The package '{package.Manifest.Id}' has no operation '{operation}'.");
        return _packages.TryGetValue((package.Manifest.Id, operation.ToUpperInvariant()), out var stub)
            ? new StubOperation(stub, manifest)
            : throw new InvalidOperationException($"No stub answers the package operation '{package.Manifest.Id}/{operation}'.");
    }

    // A failed stub answers 400, which the connector call handler treats as a failure that is not retried.
    // A call with no stub, such as a lookup, answers 404.
    private sealed class StubHandler(ScenarioStubs stubs) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Options.TryGetValue(ConnectorClient.ConnectorOption, out var connector)
                || !request.Options.TryGetValue(ConnectorClient.OperationOption, out var operation)
                || !stubs._connectors.TryGetValue((connector.ToUpperInvariant(), operation.ToUpperInvariant()), out var stub))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (stub.Outcome == ScenarioStubDefinition.Failed)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(stub.Output), Encoding.UTF8, "application/json"),
            });
        }
    }

    // A value that does not fit its manifest type is passed on as it is, so the call handler finds
    // that the output does not fit and fails the instance.
    private sealed class StubOperation(ScenarioStubDefinition stub, PackageOperation manifest) : IOperation
    {
        public Task<IReadOnlyDictionary<string, object?>> RunAsync(
            IReadOnlyDictionary<string, object?> input,
            IOperationContext context,
            CancellationToken cancellationToken)
        {
            if (stub.Outcome == ScenarioStubDefinition.Failed)
            {
                throw new InvalidOperationException("The scenario stub says the call fails.");
            }

            var output = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, element) in stub.Output)
            {
                var field = manifest.Output.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
                if (field is null)
                {
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Null)
                {
                    output[field.Name] = null;
                }
                else
                {
                    output[field.Name] = ConnectorCallHandler.TryRead(element, field.Type, out var value) ? value : element;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(output);
        }
    }
}
