namespace Axis.Server.Exports;

/// <summary>Writes the rows of an export to a stream in one file format. Call <see cref="WriteHeader"/>, then <see cref="WriteRow"/> for each row, then <see cref="Complete"/>.</summary>
internal interface IExportWriter : IDisposable
{
    /// <summary>Writes the first row, which holds the column names.</summary>
    void WriteHeader(IReadOnlyList<ExportColumn> columns);

    /// <summary>Writes one row. <paramref name="cells"/> holds one text per column, or null for no value.</summary>
    void WriteRow(IReadOnlyList<string?> cells);

    /// <summary>Finishes the file, so the stream holds all of it. The stream stays open.</summary>
    void Complete();
}
