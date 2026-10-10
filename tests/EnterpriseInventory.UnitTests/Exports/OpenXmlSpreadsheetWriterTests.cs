using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Infrastructure.Exports;
using EnterpriseInventory.Tests.Exports;

namespace EnterpriseInventory.UnitTests.Exports;

public class OpenXmlSpreadsheetWriterTests
{
    private static readonly SpreadsheetColumn[] TwoColumns = [new("Ad", 10), new("Değer", 20)];

    private static SpreadsheetFile WriteAndRead(params SpreadsheetSheet[] sheets)
    {
        var writer = new OpenXmlSpreadsheetWriter();
        using var file = new MemoryStream();
        writer.Write(new Spreadsheet("Deneme", sheets), file);
        return SpreadsheetFile.Read(file.ToArray());
    }

    [Fact]
    public void Text_numbers_dates_and_empty_cells_are_written_as_Excel_reads_them()
    {
        var at = new DateTime(2026, 10, 10, 9, 5, 0);
        var file = WriteAndRead(new SpreadsheetSheet("Liste", TwoColumns, [["Çağrı Işık", 42], [null, at], ["Şüheda", null]]));

        var sheet = file["Liste"];
        Assert.Equal(["Ad", "Değer"], sheet.Header);
        var rows = sheet.DataRows;
        Assert.Equal(("inlineStr", "Çağrı Işık"), (rows[0][0]!.Type, rows[0][0]!.Text));
        Assert.Equal(("n", 42), (rows[0][1]!.Type, rows[0][1]!.AsInt()));

        // An empty cell is left out; a date is a number Excel shows with the date format.
        Assert.Null(rows[1][0]);
        Assert.Equal(at, rows[1][1]!.AsDateTime());
        Assert.Equal(2u, rows[1][1]!.Style);
        Assert.Equal("A4", Assert.Single(rows[2])!.Reference);
        Assert.Empty(file.SchemaErrors);
    }

    [Fact]
    public void Text_that_looks_like_a_formula_stays_text()
    {
        var file = WriteAndRead(new SpreadsheetSheet("Liste", TwoColumns, [["=HYPERLINK(\"http://kotu.example\",\"tıkla\")", "+1+1"], ["@SUM(A1:A2)", "-2+3"]]));

        var cells = file["Liste"].DataRows.SelectMany(r => r).ToList();
        Assert.All(cells, cell =>
        {
            Assert.Equal("inlineStr", cell!.Type);
            Assert.False(cell.HasFormula);
        });
        Assert.Equal("=HYPERLINK(\"http://kotu.example\",\"tıkla\")", cells[0]!.Text);
        Assert.Equal("-2+3", cells[3]!.Text);
    }

    [Fact]
    public void Characters_XML_cannot_carry_are_dropped_and_long_text_is_cut_to_what_a_cell_holds()
    {
        var file = WriteAndRead(new SpreadsheetSheet(
            "Liste",
            TwoColumns,
            [["a\u0001b\u0000c\td\ne", "yüz 😀 \ud800 son"], [new string('x', OpenXmlSpreadsheetWriter.MaxCellLength + 10), "  boşluk  "]]));

        var rows = file["Liste"].DataRows;
        Assert.Equal("abc\td\ne", rows[0][0]!.Text);
        Assert.Equal("yüz 😀  son", rows[0][1]!.Text);
        Assert.Equal(OpenXmlSpreadsheetWriter.MaxCellLength, rows[1][0]!.Text.Length);
        Assert.Equal("  boşluk  ", rows[1][1]!.Text);
        Assert.Empty(file.SchemaErrors);
    }

    [Fact]
    public void A_surrogate_pair_is_never_split_when_text_is_cut()
    {
        var text = new string('x', OpenXmlSpreadsheetWriter.MaxCellLength - 1) + "😀";

        Assert.Equal(OpenXmlSpreadsheetWriter.MaxCellLength - 1, OpenXmlSpreadsheetWriter.CellText(text).Length);
    }

    [Fact]
    public void The_header_is_bold_stays_in_view_and_a_filterable_sheet_opens_with_filter_buttons()
    {
        var file = WriteAndRead(
            new SpreadsheetSheet("Envanter", TwoColumns, [["a", 1], ["b", 2], ["c", 3]]) { Filterable = true },
            new SpreadsheetSheet("Bilgi", TwoColumns, [["Rapor", "Deneme"]]));

        var list = file["Envanter"];
        Assert.All(list.Rows[0], cell => Assert.Equal(1u, cell!.Style));
        Assert.Equal("A2", list.FrozenAt);
        Assert.Equal("A1:B4", list.AutoFilter);
        Assert.Equal([10d, 20d], list.ColumnWidths);
        Assert.Equal(["_xlnm._FilterDatabase|0|True|'Envanter'!$A$1:$B$4"], file.DefinedNames);

        Assert.Null(file["Bilgi"].AutoFilter);
        Assert.Equal(["Envanter", "Bilgi"], file.Sheets.Select(s => s.Name));
        Assert.Empty(file.SchemaErrors);
    }

    [Fact]
    public void An_empty_sheet_still_has_its_header()
    {
        var file = WriteAndRead(new SpreadsheetSheet("Envanter", TwoColumns, []) { Filterable = true });

        Assert.Equal(["Ad", "Değer"], file["Envanter"].Header);
        Assert.Empty(file["Envanter"].DataRows);
        Assert.Equal("A1:B1", file["Envanter"].AutoFilter);
        Assert.Empty(file.SchemaErrors);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Columns_are_named_the_Excel_way(int column, string name) =>
        Assert.Equal(name, OpenXmlSpreadsheetWriter.ColumnName(column));

    [Theory]
    [InlineData("")]
    [InlineData("Envanter/2026")]
    [InlineData("[Bilgi]")]
    [InlineData("'Bilgi")]
    [InlineData("Otuz bir karakterden uzun bir sayfa adı")]
    public void A_sheet_name_Excel_would_refuse_is_refused(string name)
    {
        var writer = new OpenXmlSpreadsheetWriter();
        using var file = new MemoryStream();

        Assert.Throws<ArgumentException>(() => writer.Write(new Spreadsheet("Deneme", [new SpreadsheetSheet(name, TwoColumns, [])]), file));
    }

    [Fact]
    public void A_value_a_cell_cannot_hold_is_refused_rather_than_written_as_something_else()
    {
        var writer = new OpenXmlSpreadsheetWriter();
        using var first = new MemoryStream();
        using var second = new MemoryStream();

        Assert.Throws<ArgumentException>(() =>
            writer.Write(new Spreadsheet("Deneme", [new SpreadsheetSheet("Liste", TwoColumns, [[new object(), 1]])]), first));
        Assert.Throws<ArgumentException>(() =>
            writer.Write(new Spreadsheet("Deneme", [new SpreadsheetSheet("Liste", TwoColumns, [["a", 1, "fazla"]])]), second));
    }
}
