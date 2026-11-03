using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;

namespace Axis.Admin;

/// <summary>
/// Where <c>cell backup</c> uploads the archives: an S3-compatible service with path-style addressing, from the five
/// <c>Cell:BackupStorage</c> settings. Only <c>cell backup</c> reads them. A message names a setting and never holds its value,
/// and the secret access key is never printed.
/// </summary>
internal sealed record CellBackupStorage(string ServiceUrl, string Region, string Bucket, string AccessKeyId, string SecretAccessKey)
{
    private const string Section = "Cell:BackupStorage";

    /// <summary>Reads the settings. Returns null with the reason in <paramref name="problem"/> when one is blank.</summary>
    public static CellBackupStorage? Read(IConfiguration configuration, out string? problem)
    {
        var values = new List<string>();
        foreach (var name in new[] { "ServiceUrl", "Region", "Bucket", "AccessKeyId", "SecretAccessKey" })
        {
            var value = configuration[$"{Section}:{name}"];
            if (string.IsNullOrWhiteSpace(value))
            {
                problem = $"The setting {Section}:{name} is required.";
                return null;
            }

            values.Add(value);
        }

        problem = null;
        return new CellBackupStorage(values[0], values[1], values[2], values[3], values[4]);
    }

    public IAmazonS3 NewClient() =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKeyId, SecretAccessKey),
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = Region });
}
