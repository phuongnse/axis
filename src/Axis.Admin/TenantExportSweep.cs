using Amazon.S3.Model;
using Axis.Administration;

namespace Axis.Admin;

/// <summary>
/// The sweep of the publisher that keeps a tenant export for <see cref="TenantExports.Kept"/> only. Each round lists every object under
/// <c>exports/</c> in the export storage and deletes the ones that were stored that long ago or longer. It reads the storage's own
/// time of each object and never the database, so it also deletes a ZIP that has no job row. It writes one line for each deletion, with
/// the tenant and the key, and never a setting value.
/// </summary>
internal sealed class TenantExportSweep(CellBackupStorage storage)
{
    /// <summary>The time between two rounds.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public async Task RunRoundAsync(DateTimeOffset now, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        var cutoff = now - TenantExports.Kept;
        using var client = storage.NewClient();
        var request = new ListObjectsV2Request { BucketName = storage.Bucket, Prefix = TenantExports.Prefix };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response.S3Objects ?? [])
            {
                if (item.LastModified is not { } stored || new DateTimeOffset(stored.ToUniversalTime()) > cutoff)
                {
                    continue;
                }

                await client.DeleteObjectAsync(storage.Bucket, item.Key, cancellationToken);
                var tenant = item.Key.Split('/') is [_, var name, ..] ? name : "-";
                output.WriteLine($"{tenant} export deleted: {item.Key}");
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }
}
