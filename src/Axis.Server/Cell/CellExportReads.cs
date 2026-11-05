using System.Net;
using Amazon.S3;
using Axis.Administration;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// Answers the read of an export, see <c>docs/reference/tenant-lifecycle.md#exporting-a-tenant</c>. The provisioning API's exports route and
/// the tenant administrator's route share it, so both answer the same and both record the download with their caller as actor.
/// </summary>
internal static class CellExportReads
{
    public static async Task<IResult> AnswerAsync(
        string tenantId,
        Guid exportId,
        string actor,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellPlatformWriter writer,
        CellExportStorage storage,
        CancellationToken cancellationToken)
    {
        JobRow? job;
        await using (var platform = CellEndpoints.Open(dataSource))
        {
            job = await platform.Jobs.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == exportId && row.Kind == "export" && row.TenantId == tenantId, cancellationToken);
        }

        if (job is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No export of this tenant has this id.");
        }

        var succeeded = job.State == "succeeded";
        DateTimeOffset? expiresAt = succeeded && job.FinishedAt is { } finished ? finished + TenantExports.Kept : null;
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return Gone();
        }

        if (!httpContext.Request.GetTypedHeaders().Accept.Any(accept => accept.MediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Ok(new CellExportResponse(job.Id, job.TenantId, job.State, job.CreatedAt, expiresAt));
        }

        if (!succeeded)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The export is not ready.");
        }

        if (storage.Storage is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The export storage is not configured.");
        }

        var client = storage.Storage.NewClient();
        httpContext.Response.RegisterForDispose(client);
        Amazon.S3.Model.GetObjectResponse stored;
        try
        {
            stored = await client.GetObjectAsync(storage.Storage.Bucket, TenantExports.Key(tenantId, exportId), cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound || exception.ErrorCode == "NoSuchKey")
        {
            return Gone();
        }

        httpContext.Response.RegisterForDispose(stored);

        // The record comes after the storage returned the object and before the first byte, so a download that breaks half-way is
        // still recorded, and a download that finds no ZIP is not.
        await using (var platform = writer.Open())
        {
            await PlatformAuditRecords.WriteAsync(
                platform,
                TenantExports.DownloadCommand,
                tenantId,
                [$"export:{exportId:D}"],
                actor,
                cancellationToken);
        }

        return Results.Stream(stored.ResponseStream, "application/zip", $"{tenantId}-{exportId:D}.zip");

        static IResult Gone() =>
            Results.Problem(statusCode: StatusCodes.Status410Gone, title: "The export was deleted after 7 days.");
    }
}
