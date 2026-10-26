using Axis.Data.Audit;
using Axis.Server.Tenancy;
using Npgsql;

namespace Axis.Server.Exports;

/// <summary>
/// The shared steps of the export routes. The rows are written to a temporary file as they are read, so
/// no export holds every row in memory. The file is sent only when it is complete, which lets the response
/// say whether it was cut before any byte goes out, and lets the audit record come first.
/// </summary>
internal static class ExportFile
{
    /// <summary>The header that says the file stops at <c>Data:ExportMaxRows</c> although more rows match.</summary>
    public const string TruncatedHeader = "Axis-Export-Truncated";

    /// <summary>Reads <paramref name="format"/>, or adds an error at <c>format</c> when it is missing or not <c>csv</c> or <c>xlsx</c>.</summary>
    public static bool TryParseFormat(string? format, SortedDictionary<string, string[]> errors, out ExportFormat result)
    {
        if (ExportFormats.TryParse(format, out result))
        {
            return true;
        }

        errors["format"] = ["Must be 'csv' or 'xlsx'."];
        return false;
    }

    /// <summary>A new temporary file that is deleted when it is closed. Only its owner can read it.</summary>
    public static FileStream CreateTemp()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(Path.Combine(Path.GetTempPath(), $"axis-export-{Guid.NewGuid():N}.tmp"), options);
    }

    /// <summary>
    /// Writes the header and up to <paramref name="maxRows"/> rows of <paramref name="items"/> to a temporary
    /// file in <paramref name="format"/>, and sends the file in a response. <paramref name="items"/> may hold one
    /// item more than <paramref name="maxRows"/>, which only shows that the file is cut. When the file is
    /// complete, the read transaction ends and <paramref name="audit"/>, given the row count and whether the file is cut, is
    /// appended on <paramref name="connection"/> in its own transaction. A failure at any step deletes the file
    /// and sends nothing, and leaves no audit record.
    /// </summary>
    public static async Task<IResult> RunAsync<T>(
        HttpContext context,
        TenantDatabase database,
        NpgsqlConnection connection,
        ExportFormat format,
        string name,
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<T> items,
        Func<T, IReadOnlyList<string?>> cells,
        int maxRows,
        Func<int, bool, AuditEntry> audit,
        CancellationToken cancellationToken)
    {
        var file = CreateTemp();
        try
        {
            var rows = 0;
            var truncated = false;
            using (var writer = CreateWriter(format, file))
            {
                writer.WriteHeader(columns);
                await foreach (var item in items.WithCancellation(cancellationToken))
                {
                    if (rows == maxRows)
                    {
                        truncated = true;
                        break;
                    }

                    writer.WriteRow(cells(item));
                    rows++;
                }

                writer.Complete();
            }

            // The reads are done. The audit record needs a transaction that can write.
            await database.EndReadsAsync();
            await AuditAsync(connection, audit(rows, truncated), cancellationToken);
            return Result(context, file, name, format, truncated);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static IExportWriter CreateWriter(ExportFormat format, Stream stream) => format switch
    {
        ExportFormat.Csv => new CsvExportWriter(stream),
        ExportFormat.Xlsx => new XlsxExportWriter(stream),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    /// <summary>Appends <paramref name="entry"/> in a short transaction of its own, which commits before the file is sent.</summary>
    public static async Task AuditAsync(NpgsqlConnection connection, AuditEntry entry, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AuditRecords.AppendAsync(transaction, entry, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The download of <paramref name="file"/>, which is sent from its start and then disposed, so it is deleted.
    /// The response is never cached, and says when the file is cut.
    /// </summary>
    public static IResult Result(HttpContext context, Stream file, string name, ExportFormat format, bool truncated)
    {
        file.Seek(0, SeekOrigin.Begin);
        context.Response.Headers.CacheControl = "no-store";
        if (truncated)
        {
            context.Response.Headers[TruncatedHeader] = "true";
        }

        return Results.File(file, format.ContentType(), fileDownloadName: name.Replace('/', '-') + format.Extension());
    }
}
