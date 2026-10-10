namespace EnterpriseInventory.Application.Exports;

/// <summary>A workbook to download: its sheets in order. Written by <see cref="ISpreadsheetWriter"/>.</summary>
public sealed record Spreadsheet(string Title, IReadOnlyList<SpreadsheetSheet> Sheets);

/// <summary>A column: its header and its width in characters.</summary>
public sealed record SpreadsheetColumn(string Header, double Width);

/// <summary>
/// A sheet: a header row and the data rows under it. A cell is a <see cref="string"/> (always written as text, never as
/// a formula), an <see cref="int"/>, a <see cref="DateTime"/> already in the reader's time zone, or <c>null</c> for an
/// empty cell. A sheet with <see cref="Filterable"/> opens with Excel's filter buttons on its header.
/// </summary>
public sealed record SpreadsheetSheet(string Name, IReadOnlyList<SpreadsheetColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public bool Filterable { get; init; }
}

/// <summary>Turns a <see cref="Spreadsheet"/> into a file.</summary>
public interface ISpreadsheetWriter
{
    /// <summary>The media type of the file, for the response.</summary>
    string ContentType { get; }

    /// <summary>The file name extension, with its dot.</summary>
    string FileExtension { get; }

    void Write(Spreadsheet spreadsheet, Stream output);
}
