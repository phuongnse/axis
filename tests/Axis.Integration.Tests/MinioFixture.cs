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

    /// <summary>The whole object. MinIO at times ends a response early, so a body cut short is read again from the start, up to three times.</summary>
    public static Task<byte[]> ReadObjectAsync(IAmazonS3 s3, string bucket, string key, CancellationToken cancellationToken) =>
        ReadWholeAsync(token => s3.GetObjectAsync(bucket, key, token), cancellationToken);

    internal static async Task<byte[]> ReadWholeAsync(Func<CancellationToken, Task<GetObjectResponse>> get, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await get(cancellationToken);
                using var memory = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memory, cancellationToken);
                return memory.ToArray();
            }
            catch (Exception exception) when (attempt < 3 && exception is IOException or HttpRequestException)
            {
            }
        }
    }

    /// <summary>A client of the service, with path-style addressing.</summary>
    public IAmazonS3 NewClient() =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
}
