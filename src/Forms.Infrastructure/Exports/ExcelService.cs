using Skylab.Forms.Application.Abstractions;
using ClosedXML.Excel;
using Skylab.Forms.Application.Contracts.Exports;

namespace Skylab.Forms.Infrastructure.Exports;

public class ExcelService : IExcelService
{
    private const int MaxSheetNameLength = 31;
    private const int MaxCellLength = 32_767;
    private const string FallbackSheetName = "Yanıtlar";
    private static readonly char[] ForbiddenSheetNameCharacters = [':', '\\', '/', '?', '*', '[', ']'];

    public byte[] GenerateExcel(ExcelExportRequest request)
    {
        using var workbook = new XLWorkbook();

        var worksheet = workbook.Worksheets.Add(SheetName(request.SheetName));

        for (int i = 0; i < request.Headers.Count; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = request.Headers[i];
            cell.Style.Font.Bold = true;
        }

        for (int r = 0; r < request.Rows.Count; r++)
        {
            for (int c = 0; c < request.Rows[r].Count; c++)
            {
                worksheet.Cell(r + 2, c + 1).Value = CellText(request.Rows[r][c]);
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    // Excel sayfa adında : \ / ? * [ ] olamaz, ad en çok 31 karakterdir, kesme işaretiyle başlayıp bitemez ve
    // History adı Excel'e ayrılmıştır.
    private static string SheetName(string? title)
    {
        var name = string.Concat((title ?? string.Empty).Select(ch => ForbiddenSheetNameCharacters.Contains(ch) ? '-' : ch)).Trim().Trim('\'');

        if (name.Length > MaxSheetNameLength) name = Cut(name, MaxSheetNameLength).TrimEnd().TrimEnd('\'');

        return name.Length == 0 || name.Equals("History", StringComparison.OrdinalIgnoreCase) ? FallbackSheetName : name;
    }

    // Bir hücre en çok 32.767 karakter taşır; uzun cevap bütün dosyayı düşürmesin, kesildiği görünsün.
    private static string CellText(string value) =>
        value.Length <= MaxCellLength ? value : Cut(value, MaxCellLength - 1) + "…";

    // Emoji gibi iki UTF-16 birimlik bir karakter ortadan bölünürse dosya kaydedilemez.
    private static string Cut(string value, int length) =>
        char.IsHighSurrogate(value[length - 1]) ? value[..(length - 1)] : value[..length];
}
