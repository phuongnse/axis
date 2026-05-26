using Amazon.S3;
using Amazon.S3.Model;
using Axis.Identity.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class S3OrganizationLogoStorageService(IAmazonS3 s3, IConfiguration configuration)
    : IOrganizationLogoStorageService
{
    private string BucketName => configuration["Aws:S3:OrganizationLogoBucket"]
        ?? configuration["Aws:S3:AvatarBucket"]
        ?? "axis-avatars";

    public async Task<string> UploadLogoAsync(
        Guid organizationId,
        byte[] data,
        string contentType,
        CancellationToken ct = default)
    {
        string key = $"org-logos/{organizationId}/{Guid.NewGuid()}";

        PutObjectRequest request = new()
        {
            BucketName = BucketName,
            Key = key,
            ContentType = contentType,
            InputStream = new MemoryStream(data),
        };

        await s3.PutObjectAsync(request, ct);
        return $"https://{BucketName}.s3.amazonaws.com/{key}";
    }

    public async Task DeleteLogoAsync(string logoUrl, CancellationToken ct = default)
    {
        Uri uri = new(logoUrl);
        string bucket = ResolveBucketName(uri);
        string key = ResolveObjectKey(uri);
        await s3.DeleteObjectAsync(bucket, key, ct);
    }

    private static string ResolveObjectKey(Uri logoUri)
    {
        string host = logoUri.Host;

        if (host.EndsWith(".s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
            return logoUri.AbsolutePath.TrimStart('/');

        if (host.StartsWith("s3.", StringComparison.OrdinalIgnoreCase) ||
            host.Contains(".s3.", StringComparison.OrdinalIgnoreCase))
        {
            string[] segments = logoUri.AbsolutePath.Trim('/').Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2)
                return segments[1];
        }

        throw new InvalidOperationException($"Cannot resolve S3 object key from logo URL: {logoUri}");
    }

    private static string ResolveBucketName(Uri logoUri)
    {
        string host = logoUri.Host;

        // Virtual-hosted–style: https://{bucket}.s3.amazonaws.com/{key}
        if (host.EndsWith(".s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
            return host[..^".s3.amazonaws.com".Length];

        // Path-style: https://s3.amazonaws.com/{bucket}/{key} or regional variant
        string[] segments = logoUri.AbsolutePath.Trim('/').Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && (host.StartsWith("s3.", StringComparison.OrdinalIgnoreCase) || host.Contains(".s3.", StringComparison.OrdinalIgnoreCase)))
            return segments[0];

        throw new InvalidOperationException($"Cannot resolve S3 bucket from logo URL: {logoUri}");
    }
}
