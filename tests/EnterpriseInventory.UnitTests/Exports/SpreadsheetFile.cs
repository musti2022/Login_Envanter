using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace EnterpriseInventory.Tests.Exports;

/// <summary>
/// An .xlsx file read back with the Open XML SDK, cell by cell, the way Excel would see it. Shared by the unit and
/// integration tests (the integration project links this file).
/// </summary>
internal sealed class SpreadsheetFile
{
    private SpreadsheetFile(IReadOnlyList<SheetContent> sheets, IReadOnlyList<string> schemaErrors, IReadOnlyList<string> definedNames)
    {
        Sheets = sheets;
        SchemaErrors = schemaErrors;
        DefinedNames = definedNames;
    }

    public IReadOnlyList<SheetContent> Sheets { get; }

    /// <summary>What the Open XML schema validator finds wrong with the file (Excel 2019 rules); empty when nothing.</summary>
    public IReadOnlyList<string> SchemaErrors { get; }

    /// <summary>Workbook names as <c>name|localSheetId|hidden|text</c>.</summary>
    public IReadOnlyList<string> DefinedNames { get; }

    public SheetContent this[string name] => Sheets.Single(s => s.Name == name);

    public static SpreadsheetFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var workbookPart = document.WorkbookPart!;
        var sheets = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Select(sheet => ReadSheet(sheet.Name!.Value!, (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)))
            .ToList();
        var schemaErrors = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019)
            .Validate(document)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        var definedNames = workbookPart.Workbook!.DefinedNames?.Elements<DefinedName>()
            .Select(n => $"{n.Name}|{n.LocalSheetId?.Value}|{n.Hidden?.Value}|{n.Text}")
            .ToList() ?? [];
        return new SpreadsheetFile(sheets, schemaErrors, definedNames);
    }

    private static SheetContent ReadSheet(string name, WorksheetPart part)
    {
        var worksheet = part.Worksheet!;
        var rows = new List<IReadOnlyList<CellContent?>>();
        foreach (var row in worksheet.GetFirstChild<SheetData>()!.Elements<Row>())
        {
            var cells = new List<CellContent?>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference!.Value!);
                while (cells.Count < column)
                {
                    cells.Add(null);
                }

                cells.Add(new CellContent(
                    cell.CellReference.Value!,
                    cell.DataType?.InnerText,
                    cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.InnerText : cell.CellValue?.Text ?? string.Empty,
                    cell.StyleIndex?.Value,
                    cell.CellFormula is not null));
            }

            rows.Add(cells);
        }

        var pane = worksheet.Descendants<Pane>().SingleOrDefault();
        return new SheetContent(
            name,
            rows,
            worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value,
            pane?.State?.Value == PaneStateValues.Frozen ? pane.TopLeftCell?.Value : null,
            worksheet.Descendants<Column>().Select(c => c.Width?.Value ?? 0).ToList());
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsAsciiLetterUpper))
        {
            index = (index * 26) + (c - 'A' + 1);
        }

        return index - 1;
    }

    internal sealed record SheetContent(
        string Name,
        IReadOnlyList<IReadOnlyList<CellContent?>> Rows,
        string? AutoFilter,
        string? FrozenAt,
        IReadOnlyList<double> ColumnWidths)
    {
        public IReadOnlyList<string> Header => Rows[0].Select(c => c?.Text ?? string.Empty).ToList();

        public IReadOnlyList<IReadOnlyList<CellContent?>> DataRows => Rows.Skip(1).ToList();

        /// <summary>A two-column sheet (label, value) as a lookup by label.</summary>
        public CellContent? Value(string label) => Rows.Skip(1).Single(r => r[0]?.Text == label) is var row && row.Count > 1 ? row[1] : null;
    }

    /// <param name="Type">The cell's data type as written: <c>inlineStr</c>, <c>n</c>.</param>
    internal sealed record CellContent(string Reference, string? Type, string Text, uint? Style, bool HasFormula)
    {
        public DateTime AsDateTime() => DateTime.FromOADate(double.Parse(Text, CultureInfo.InvariantCulture));

        public int AsInt() => int.Parse(Text, CultureInfo.InvariantCulture);
    }
}
