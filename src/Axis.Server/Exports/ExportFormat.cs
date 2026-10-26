namespace Axis.Server.Exports;

/// <summary>The file formats of an export, named as <c>?format=</c> spells them.</summary>
internal enum ExportFormat
{
    Csv,
    Xlsx,
}

internal static class ExportFormats
{
    public static string ContentType(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => "text/csv; charset=utf-8",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    /// <summary>The file extension with its dot.</summary>
    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => ".csv",
        ExportFormat.Xlsx => ".xlsx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    /// <summary>The value of <c>?format=</c> for <paramref name="format"/>, as the audit record names it.</summary>
    public static string Name(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => "csv",
        ExportFormat.Xlsx => "xlsx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    /// <summary>Reads <c>csv</c> or <c>xlsx</c>, exactly as written in lower case.</summary>
    public static bool TryParse(string? text, out ExportFormat format)
    {
        (var known, format) = text switch
        {
            "csv" => (true, ExportFormat.Csv),
            "xlsx" => (true, ExportFormat.Xlsx),
            _ => (false, default(ExportFormat)),
        };
        return known;
    }
}
