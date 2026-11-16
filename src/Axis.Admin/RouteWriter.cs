using System.Text.Json.Nodes;

namespace Axis.Admin;

/// <summary>Creates the route of a verified tenant host, so that tests can answer in place of the cluster.</summary>
internal interface IRouteWriter
{
    /// <summary>
    /// Writes the route of <paramref name="host"/> for <paramref name="tenantId"/>. A route that already exists is success.
    /// Any other failure throws.
    /// </summary>
    Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken);
}

/// <summary>
/// The writer of the product, step 4 of <see href="docs/reference/cells.md#hosts-and-certificates">hosts and certificates</see>.
/// It creates a <c>networking.k8s.io/v1</c> Ingress through the Kubernetes API, named after the host, with the annotation
/// <c>axis/tenant</c>. The Ingress serves TLS from the secret named after the host and sends every path to the <c>https</c> port of the
/// server Service. A blank class leaves <c>ingressClassName</c> out, so the default class of the cluster applies. The tenant annotation
/// wins over a configured one, because it ties the route to its tenant. The name makes a second create a <c>409</c>, which counts as done.
/// </summary>
internal sealed class KubernetesRouteWriter(KubernetesApi api, string service, string? ingressClass, IReadOnlyDictionary<string, string> annotations) : IRouteWriter
{
    public Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken)
    {
        var metadataAnnotations = new JsonObject();
        foreach (var (key, value) in annotations)
        {
            metadataAnnotations[key] = value;
        }

        metadataAnnotations["axis/tenant"] = tenantId;
        var spec = new JsonObject();
        if (!string.IsNullOrWhiteSpace(ingressClass))
        {
            spec["ingressClassName"] = ingressClass;
        }

        spec["tls"] = new JsonArray(new JsonObject { ["hosts"] = new JsonArray(host), ["secretName"] = host });
        spec["rules"] = new JsonArray(new JsonObject
        {
            ["host"] = host,
            ["http"] = new JsonObject
            {
                ["paths"] = new JsonArray(new JsonObject
                {
                    ["path"] = "/",
                    ["pathType"] = "Prefix",
                    ["backend"] = new JsonObject
                    {
                        ["service"] = new JsonObject { ["name"] = service, ["port"] = new JsonObject { ["name"] = "https" } },
                    },
                }),
            },
        });
        var body = new JsonObject
        {
            ["apiVersion"] = "networking.k8s.io/v1",
            ["kind"] = "Ingress",
            ["metadata"] = new JsonObject { ["name"] = host, ["annotations"] = metadataAnnotations },
            ["spec"] = spec,
        };
        return api.CreateAsync("apis/networking.k8s.io/v1", "ingresses", body, "route", cancellationToken);
    }
}
