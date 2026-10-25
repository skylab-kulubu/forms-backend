using ClosedXML.Excel;
using Skylab.Forms.Application.Contracts.Exports;
using Skylab.Forms.Infrastructure.Exports;

namespace Skylab.Forms.Tests.Infrastructure;

public class ExcelServiceTests
{
    private static IXLWorksheet Export(string title, string answer = "Mehmet")
    {
        var file = new ExcelService().GenerateExcel(new ExcelExportRequest(title, ["Cevap"], [[answer]]));

        return new XLWorkbook(new MemoryStream(file)).Worksheets.Single();
    }

    [Theory]
    [InlineData("2026/27 Başvuru: Ekim [ön]", "2026-27 Başvuru- Ekim -ön-")]
    [InlineData("'Tırnaklı'", "Tırnaklı")]
    [InlineData("History", "Yanıtlar")]
    [InlineData("   ", "Yanıtlar")]
    public void The_sheet_name_follows_excels_rules(string title, string expected) =>
        Assert.Equal(expected, Export(title).Name);

    [Fact]
    public void A_cut_sheet_name_never_splits_an_emoji()
    {
        var name = Export("Kulüp Tanışma Etkinliği Kayıt 🎉 2026").Name;

        Assert.True(name.Length <= 31);
        Assert.False(char.IsHighSurrogate(name[^1]));
    }

    [Fact]
    public void An_answer_longer_than_a_cell_holds_is_cut_and_marked()
    {
        var cell = Export("Başvuru", new string('a', 40_000)).Cell(2, 1).GetString();

        Assert.Equal(32_767, cell.Length);
        Assert.EndsWith("…", cell);
    }
}
