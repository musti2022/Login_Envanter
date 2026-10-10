using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EnterpriseInventory.Application.Exports;

namespace EnterpriseInventory.Infrastructure.Exports;

/// <summary>
/// Writes an Excel workbook (.xlsx) with the Open XML SDK. Rows are streamed, so a large sheet is never held as an
/// XML tree. Text is always written as a text cell: a value such as <c>=HYPERLINK(...)</c> is shown as typed and never
/// becomes a formula. Each sheet's header row is bold and stays in view; a filterable sheet opens with Excel's filter
/// buttons on it.
/// </summary>
internal sealed class OpenXmlSpreadsheetWriter : ISpreadsheetWriter
{
    /// <summary>Excel's limit on the characters of one cell.</summary>
    internal const int MaxCellLength = 32_767;

    // Indexes into the cell formats of the stylesheet below.
    private const uint HeaderStyle = 1;
    private const uint DateTimeStyle = 2;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string FileExtension => ".xlsx";

    public void Write(Spreadsheet spreadsheet, Stream output)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        ArgumentNullException.ThrowIfNull(output);

        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        document.PackageProperties.Title = spreadsheet.Title;

        var workbookPart = document.AddWorkbookPart();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var sheets = new Sheets();
        var definedNames = new DefinedNames();
        for (var i = 0; i < spreadsheet.Sheets.Count; i++)
        {
            var sheet = spreadsheet.Sheets[i];
            CheckSheetName(sheet.Name);
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            WriteSheet(worksheetPart, sheet, selected: i == 0);
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = (uint)(i + 1), Name = sheet.Name });

            if (sheet.Filterable)
            {
                // Excel keeps a filter's range in this hidden name; without it some versions repair the file.
                definedNames.Append(new DefinedName($"'{sheet.Name.Replace("'", "''", StringComparison.Ordinal)}'!{FilterRange(sheet, absolute: true)}")
                {
                    Name = "_xlnm._FilterDatabase",
                    LocalSheetId = (uint)i,
                    Hidden = true,
                });
            }
        }

        workbookPart.Workbook = definedNames.HasChildren ? new Workbook(sheets, definedNames) : new Workbook(sheets);
        workbookPart.Workbook.Save();
    }

    private static void WriteSheet(WorksheetPart part, SpreadsheetSheet sheet, bool selected)
    {
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());

        // The header row stays in view while scrolling.
        writer.WriteElement(new SheetViews(
            new SheetView(
                new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
                new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
            {
                TabSelected = selected,
                WorkbookViewId = 0,
            }));

        var columns = new Columns();
        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = sheet.Columns[c].Width, CustomWidth = true });
        }

        writer.WriteElement(columns);

        writer.WriteStartElement(new SheetData());
        writer.WriteStartElement(new Row { RowIndex = 1 });
        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            writer.WriteElement(TextCell(Reference(c, 1), sheet.Columns[c].Header, HeaderStyle));
        }

        writer.WriteEndElement();

        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var rowIndex = (uint)(r + 2);
            var values = sheet.Rows[r];
            if (values.Count > sheet.Columns.Count)
            {
                throw new ArgumentException($"Row {rowIndex} of sheet '{sheet.Name}' has more cells than columns.", nameof(sheet));
            }

            writer.WriteStartElement(new Row { RowIndex = rowIndex });
            for (var c = 0; c < values.Count; c++)
            {
                if (ToCell(Reference(c, rowIndex), values[c]) is { } cell)
                {
                    writer.WriteElement(cell);
                }
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        if (sheet.Filterable)
        {
            writer.WriteElement(new AutoFilter { Reference = FilterRange(sheet, absolute: false) });
        }

        writer.WriteEndElement();
    }

    /// <summary>An empty cell is left out.</summary>
    private static Cell? ToCell(string reference, object? value) => value switch
    {
        null => null,
        string text => TextCell(reference, text, styleIndex: null),
        int number => NumberCell(reference, number.ToString(CultureInfo.InvariantCulture)),
        long number => NumberCell(reference, number.ToString(CultureInfo.InvariantCulture)),
        decimal number => NumberCell(reference, number.ToString(CultureInfo.InvariantCulture)),
        double number when double.IsFinite(number) => NumberCell(reference, number.ToString("R", CultureInfo.InvariantCulture)),
        DateTime moment => new Cell
        {
            CellReference = reference,
            DataType = CellValues.Number,
            StyleIndex = DateTimeStyle,
            CellValue = new CellValue(moment.ToOADate().ToString("R", CultureInfo.InvariantCulture)),
        },
        _ => throw new ArgumentException($"A cell cannot hold a {value.GetType().Name}.", nameof(value)),
    };

    private static Cell NumberCell(string reference, string value) =>
        new() { CellReference = reference, DataType = CellValues.Number, CellValue = new CellValue(value) };

    /// <summary>An inline text cell: shown as written, never evaluated.</summary>
    private static Cell TextCell(string reference, string text, uint? styleIndex)
    {
        var cell = new Cell
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(CellText(text)) { Space = SpaceProcessingModeValues.Preserve }),
        };
        if (styleIndex is { } style)
        {
            cell.StyleIndex = style;
        }

        return cell;
    }

    /// <summary>
    /// The text without characters XML cannot carry (control characters other than tab and line breaks; unpaired
    /// surrogates), cut to what one cell can hold.
    /// </summary>
    internal static string CellText(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaxCellLength));
        for (var i = 0; i < text.Length && builder.Length < MaxCellLength; i++)
        {
            var c = text[i];
            if (XmlConvert.IsXmlChar(c))
            {
                builder.Append(c);
            }
            else if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
            {
                if (builder.Length + 2 > MaxCellLength)
                {
                    break;
                }

                builder.Append(c).Append(text[i + 1]);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>The header and every row, e.g. <c>A1:O42</c> (or <c>$A$1:$O$42</c>).</summary>
    private static string FilterRange(SpreadsheetSheet sheet, bool absolute)
    {
        var lastColumn = ColumnName(sheet.Columns.Count - 1);
        var lastRow = (sheet.Rows.Count + 1).ToString(CultureInfo.InvariantCulture);
        return absolute ? $"$A$1:${lastColumn}${lastRow}" : $"A1:{lastColumn}{lastRow}";
    }

    private static string Reference(int column, uint row) => ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    /// <summary>A, B, …, Z, AA, AB, … for the zero-based column.</summary>
    internal static string ColumnName(int column)
    {
        var name = string.Empty;
        for (var n = column + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>Excel refuses a workbook whose sheet names break its rules.</summary>
    private static void CheckSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 31 || name.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) >= 0 || name.StartsWith('\''))
        {
            throw new ArgumentException($"'{name}' is not a valid Excel sheet name.", nameof(name));
        }
    }

    private static Stylesheet CreateStylesheet() =>
        new(
            new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy hh:mm" }) { Count = 1 },
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
            {
                Count = 2,
            },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8EEF6" }) { PatternType = PatternValues.Solid }))
            {
                Count = 3,
            },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 2, BorderId = 0, FormatId = 0, ApplyFont = true, ApplyFill = true },
                new CellFormat { NumberFormatId = 164, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true })
            {
                Count = 3,
            },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
