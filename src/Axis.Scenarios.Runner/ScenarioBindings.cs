using Axis.Configuration.Model;
using Axis.Data;
using Axis.Tenancy;

namespace Axis.Scenarios.Runner;

/// <summary>
/// The deployment bindings of the scenario tenant. Every <c>url</c> binding is a placeholder host
/// that nothing listens on, and every <c>secret</c> binding has a placeholder value. A scenario never
/// holds a real URL or secret, and its stubs answer the calls that would use them.
/// </summary>
internal sealed class ScenarioBindings : IDeploymentBindings
{
    private const string Url = "https://scenario.invalid";
    private const string Secret = "scenario";

    public DeploymentBinding Resolve(string name) => new(name, ActivationBinding.UrlKind, Url);

    public SecretValue ResolveSecret(string name) => new(Secret);

    /// <summary>
    /// The bindings activation checks: a <c>url</c> binding for each connector base URL and package
    /// <c>url</c> capability, and a <c>secret</c> binding for each connector auth and package
    /// <c>secret</c> capability the application names.
    /// </summary>
    public static ActivationBindings ForActivation(ApplicationModel model)
    {
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase);
        var url = new ActivationBinding(ActivationBinding.UrlKind, new Uri(Url));
        var secret = new ActivationBinding(ActivationBinding.SecretKind, null);
        foreach (var connector in model.Connectors)
        {
            bindings[connector.BaseUrlBinding] = url;
            if (connector.Auth is { } auth)
            {
                bindings[auth.Binding] = secret;
            }
        }

        foreach (var step in model.Processes.SelectMany(process => process.Steps).OfType<PackageOperationStepModel>())
        {
            foreach (var name in step.Capabilities.Urls)
            {
                bindings[name] = url;
            }

            foreach (var name in step.Capabilities.Secrets)
            {
                bindings[name] = secret;
            }
        }

        return new ActivationBindings(ScenarioRunner.TenantId, bindings, AllowHttp: false);
    }
}
