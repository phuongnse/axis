using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// A stand-in for a cell: a Kestrel server on a free local port that requires a client certificate and accepts only the one
/// it issued. It serves a fake token endpoint, two <c>POST</c> routes and the job read, and records what it sees. The
/// certificates are made fresh for each server, and written as PEM files to a folder of their own.
/// </summary>
internal sealed class CellTestServer : IAsyncDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("axis-cell-server").FullName;
    private readonly ConcurrentQueue<SeenRequest> _posts = new();
    private readonly ConcurrentQueue<string> _tokenRequests = new();
    private readonly List<string> _jobStates = [];
    private WebApplication _app = null!;
    private int _tokenCount;
    private int _refusals;
    private int _jobReads;

    private CellTestServer()
    {
    }

    public Uri InternalUrl { get; private set; } = null!;

    public Uri TokenUrl => new(InternalUrl, "/realms/operator/protocol/openid-connect/token");

    public string CertificatePath => Path.Combine(_folder, "client.crt");

    public string KeyPath => Path.Combine(_folder, "client.key");

    public string CertificateAuthorityPath => Path.Combine(_folder, "ca.crt");

    /// <summary>The <c>expires_in</c> of the tokens of the fake token endpoint, in seconds.</summary>
    public int TokenLifetimeSeconds { get; set; } = 3600;

    /// <summary>The tokens issued so far.</summary>
    public int TokenCount => Volatile.Read(ref _tokenCount);

    /// <summary>The <c>Authorization</c> header of each token request.</summary>
    public IReadOnlyCollection<string> TokenRequests => _tokenRequests;

    /// <summary>What each run of the <c>POST</c> route saw.</summary>
    public IReadOnlyCollection<SeenRequest> Posts => _posts;

    public int JobReads => Volatile.Read(ref _jobReads);

    /// <summary>When set, the create route answers <c>409</c> with this title. Otherwise it queues a job.</summary>
    public string? ConflictTitle { get; set; }

    /// <summary>Makes the next <paramref name="count"/> calls to the cell, the token requests excepted, answer <c>401</c>.</summary>
    public void RefuseNext(int count) => Volatile.Write(ref _refusals, count);

    /// <summary>The state of each read of the job. The last state repeats.</summary>
    public void ScriptJob(params string[] states)
    {
        lock (_jobStates)
        {
            _jobStates.Clear();
            _jobStates.AddRange(states);
        }
    }

    public static async Task<CellTestServer> StartAsync()
    {
        var server = new CellTestServer();
        await server.InitializeAsync();
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task InitializeAsync()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Axis test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));

        using var serverCertificate = Issue(ca, "CN=localhost", "1.3.6.1.5.5.7.3.1", "localhost");
        using var clientCertificate = Issue(ca, "CN=axis-controlplane", "1.3.6.1.5.5.7.3.2", null);
        await File.WriteAllTextAsync(CertificatePath, clientCertificate.ExportCertificatePem());
        await File.WriteAllTextAsync(KeyPath, clientCertificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());
        await File.WriteAllTextAsync(CertificateAuthorityPath, ca.ExportCertificatePem());
        var thumbprint = clientCertificate.Thumbprint;
        var listenerCertificate = X509CertificateLoader.LoadPkcs12(serverCertificate.Export(X509ContentType.Pfx), null);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
        {
            ServerCertificate = listenerCertificate,
            ClientCertificateMode = ClientCertificateMode.RequireCertificate,
            ClientCertificateValidation = (certificate, _, _) => certificate.Thumbprint == thumbprint,
        })));
        _app = builder.Build();
        Map(_app);
        await _app.StartAsync();
        InternalUrl = new Uri($"https://localhost:{new Uri(_app.Urls.First()).Port}");
    }

    /// <summary>A certificate for <paramref name="usage"/> that <paramref name="ca"/> signed, with its private key.</summary>
    private static X509Certificate2 Issue(X509Certificate2 ca, string subject, string usage, string? dnsName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
        if (dnsName is not null)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(dnsName);
            request.CertificateExtensions.Add(names.Build());
        }

        using var issued = request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(8));
        return X509CertificateLoader.LoadPkcs12(issued.CopyWithPrivateKey(key).Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private void Map(WebApplication app)
    {
        app.MapPost("/realms/operator/protocol/openid-connect/token", (HttpContext context) =>
        {
            _tokenRequests.Enqueue(context.Request.Headers.Authorization.ToString());
            var number = Interlocked.Increment(ref _tokenCount);
            return Results.Json(new { access_token = $"token-{number}", expires_in = TokenLifetimeSeconds, token_type = "Bearer" });
        });

        app.MapPost("/api/cell/tenants/{id}/suspend", async (HttpContext context, string id) =>
        {
            if (Refuse())
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            using var reader = new StreamReader(context.Request.Body);
            _posts.Enqueue(new SeenRequest(
                context.Request.Headers.Authorization.ToString(),
                context.Request.Headers["Idempotency-Key"].ToString(),
                await reader.ReadToEndAsync(),
                context.Connection.ClientCertificate?.Thumbprint));
            return Results.Json(
                new { id = Guid.Parse("11111111-1111-1111-1111-111111111111"), kind = "suspend", tenantId = id, state = "queued", createdAt = DateTimeOffset.UnixEpoch },
                statusCode: StatusCodes.Status202Accepted);
        });

        app.MapPost("/api/cell/tenants", () =>
        {
            if (Refuse())
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            return ConflictTitle is { } title
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title)
                : Results.Json(
                    new { id = Guid.Parse("11111111-1111-1111-1111-111111111111"), kind = "create", tenantId = "acme", state = "queued", createdAt = DateTimeOffset.UnixEpoch },
                    statusCode: StatusCodes.Status202Accepted);
        });

        app.MapGet("/api/cell/jobs/{id:guid}", (Guid id) =>
        {
            if (Refuse())
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            string state;
            lock (_jobStates)
            {
                state = _jobStates.Count == 0 ? "queued" : _jobStates[Math.Min(_jobReads, _jobStates.Count - 1)];
                _jobReads++;
            }

            var failed = state == "failed";
            return Results.Json(new
            {
                id,
                kind = "suspend",
                tenantId = "acme",
                state,
                createdAt = DateTimeOffset.UnixEpoch,
                steps = new object[]
                {
                    new
                    {
                        name = "status",
                        state = failed ? "failed" : state == "succeeded" ? "succeeded" : "pending",
                        error = failed ? "The tenant is still provisioning." : null,
                    },
                },
                error = failed ? "The tenant is still provisioning." : null,
            });
        });
    }

    private bool Refuse()
    {
        while (true)
        {
            var remaining = Volatile.Read(ref _refusals);
            if (remaining <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _refusals, remaining - 1, remaining) == remaining)
            {
                return true;
            }
        }
    }
}

/// <summary>What the <c>POST</c> route saw.</summary>
internal sealed record SeenRequest(string Authorization, string IdempotencyKey, string Body, string? ClientCertificateThumbprint);
