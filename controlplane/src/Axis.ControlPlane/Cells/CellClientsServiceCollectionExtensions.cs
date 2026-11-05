using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.ControlPlane.Cells;

internal static class CellClientsServiceCollectionExtensions
{
    public const string HttpClientName = "cells";

    /// <summary>
    /// Registers <see cref="CellClients"/>. With cells listed, every call, the token request included, goes through one
    /// handler that presents the control plane's client certificate and follows no redirect. With
    /// <c>ControlPlane:CellCertificateAuthorityPath</c> it trusts only the certificates that chain to that authority.
    /// </summary>
    public static IServiceCollection AddCellClients(this IServiceCollection services, ControlPlaneOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new CellClientOptions());
        services.AddSingleton<CellClients>();
        var http = services.AddHttpClient(HttpClientName);
        if (options.Cells.Count > 0)
        {
            var handler = CreateHandler(options);
            http.ConfigurePrimaryHttpMessageHandler(() => handler);
            // The handler lives as long as the service, so its pool is not rotated and disposed under the clients.
            http.SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        }

        return services;
    }

    private static SocketsHttpHandler CreateHandler(ControlPlaneOptions options)
    {
        var certificate = options.CellClientCertificate;
        var ssl = new SslClientAuthenticationOptions();
        try
        {
            ssl.ClientCertificates = [X509Certificate2.CreateFromPemFile(certificate!.Path!, certificate.KeyPath)];
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            throw new InvalidOperationException(
                $"The settings {ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.CellClientCertificate)} do not hold a valid PEM certificate and private key.");
        }

        if (!string.IsNullOrWhiteSpace(options.CellCertificateAuthorityPath))
        {
            var authorities = new X509Certificate2Collection();
            try
            {
                authorities.ImportFromPemFile(options.CellCertificateAuthorityPath);
            }
            catch (Exception exception) when (exception is CryptographicException or IOException)
            {
                throw new InvalidOperationException(
                    $"The setting {ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.CellCertificateAuthorityPath)} does not name a valid PEM certificate file.");
            }

            // The region's internal authority publishes no revocation list, so the chain is checked against it and nothing more.
            var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
            policy.CustomTrustStore.AddRange(authorities);
            ssl.CertificateChainPolicy = policy;
        }

        return new SocketsHttpHandler { AllowAutoRedirect = false, SslOptions = ssl };
    }
}
