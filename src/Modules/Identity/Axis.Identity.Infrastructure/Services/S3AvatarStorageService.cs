using Amazon.S3;
using Amazon.S3.Model;
using Axis.Identity.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class S3AvatarStorageService(IAmazonS3 s3, IConfiguration configuration) : IAvatarStorageService
{
    private string BucketName => configuration["Aws:S3:AvatarBucket"] ?? "axis-avatars";

    public async Task<string> UploadAvatarAsync(Guid userId, byte[] data, string contentType, CancellationToken ct = default)
    {
        var key = $"avatars/{userId}/{Guid.NewGuid()}";

        var request = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentType = contentType,
            InputStream = new MemoryStream(data)
        };

        await s3.PutObjectAsync(request, ct);

        return $"https://{BucketName}.s3.amazonaws.com/{key}";
    }

    public async Task DeleteAvatarAsync(string avatarUrl, CancellationToken ct = default)
    {
        var uri = new Uri(avatarUrl);
        var key = uri.AbsolutePath.TrimStart('/');

        await s3.DeleteObjectAsync(BucketName, key, ct);
    }
}
