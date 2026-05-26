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
        string key = uri.AbsolutePath.TrimStart('/');
        await s3.DeleteObjectAsync(BucketName, key, ct);
    }
}
