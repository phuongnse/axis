using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Axis.Integration.Tests;

/// <summary>
/// One real MinIO container, an S3-compatible object store, for the tests of a class. It supports object-locked buckets, as the
/// backup storage of a cell is, and server-side encryption. The fixture starts the container before the class's first test and removes it after the last.
/// Each test makes a bucket of its own, so no test sees another's objects.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    // MinIO no longer publishes its images on Docker Hub, so this is the last Bitnami build, which keeps its tag.
    public const string Image = "bitnamilegacy/minio:2025.7.23";

    private const int ApiContainerPort = 9000;

    private static readonly TimeSpan[] _waits = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly IContainer _container;

    public MinioFixture()
    {
        AccessKey = $"axis{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        SecretKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _container = new ContainerBuilder(Image)
            .WithEnvironment("MINIO_ROOT_USER", AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)

            // MinIO accepts server-side encryption only when it has a key, and the tenant export stores its ZIP encrypted.
            .WithEnvironment("MINIO_KMS_SECRET_KEY", "axis-test:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .WithPortBinding(ApiContainerPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiContainerPort).ForPath("/minio/health/ready")))
            .Build();
    }

    /// <summary>The address of the service, with the published port.</summary>
    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiContainerPort)}";

    public string AccessKey { get; }

    public string SecretKey { get; }

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _container.StartAsync(cancellationToken);

        // The image starts the server once to set it up, so the health check can pass before the final server answers requests.
        using var client = NewClient();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await client.ListBucketsAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < 30 && exception is AmazonServiceException or HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>The whole object. When MinIO drops a connection, answers that the client disconnected or ends a response early, the read starts again from the start, up to five tries.</summary>
    public static Task<byte[]> ReadObjectAsync(IAmazonS3 s3, string bucket, string key, CancellationToken cancellationToken) =>
        ReadWholeAsync(token => s3.GetObjectAsync(bucket, key, token), cancellationToken);

    internal static async Task<byte[]> ReadWholeAsync(Func<CancellationToken, Task<GetObjectResponse>> get, CancellationToken cancellationToken, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        byte[] bytes = [];
        await RetryDroppedAsync(
            async token =>
            {
                using var response = await get(token);
                using var memory = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memory, token);
                bytes = memory.ToArray();
            },
            wait,
            cancellationToken);
        return bytes;
    }

    /// <summary>Stores the whole object. When MinIO drops a connection or answers that the client disconnected during the upload, the upload starts again from the first byte, up to five tries.</summary>
    public static Task UploadObjectAsync(IAmazonS3 s3, string bucket, string key, byte[] bytes, CancellationToken cancellationToken) =>
        UploadWholeAsync((request, token) => s3.PutObjectAsync(request, token), bucket, key, bytes, cancellationToken);

    internal static Task UploadWholeAsync(Func<PutObjectRequest, CancellationToken, Task> put, string bucket, string key, byte[] bytes, CancellationToken cancellationToken, Func<TimeSpan, CancellationToken, Task>? wait = null) =>
        RetryDroppedAsync(
            async token =>
            {
                using var content = new MemoryStream(bytes);
                await put(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = content }, token);
            },
            wait,
            cancellationToken);

    private static async Task RetryDroppedAsync(Func<CancellationToken, Task> attempt, Func<TimeSpan, CancellationToken, Task>? wait, CancellationToken cancellationToken)
    {
        wait ??= Task.Delay;
        for (var tries = 0; ; tries++)
        {
            try
            {
                await attempt(cancellationToken);
                return;
            }
            catch (Exception exception) when (tries < _waits.Length && IsDroppedConnection(exception))
            {
                await wait(_waits[tries], cancellationToken);
            }
        }
    }

    internal static bool IsDroppedConnection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException or HttpRequestException or SocketException or AmazonS3Exception { ErrorCode: "ClientDisconnected" } or AmazonS3Exception { StatusCode: (HttpStatusCode)499 })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A client of the service, with path-style addressing.</summary>
    public IAmazonS3 NewClient() =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
}
