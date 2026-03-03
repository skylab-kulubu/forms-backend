using ClosedXML.Excel;
using Skylab.Shared.Application.Contracts;
using Skylab.Shared.Domain.Enums;
using Skylab.Exports.Application.Contracts;

namespace Skylab.Exports.Application.Services;

public class ExcelService : IExcelService
{
    public ServiceResult<byte[]> GenerateExcel(ExcelExportRequest request)
    {
        try
        {
            using var workbook = new XLWorkbook();

            var sheetName = string.IsNullOrWhiteSpace(request.SheetName) ? "Skylab_Excel_Dosyasi" : request.SheetName;
            var worksheet = workbook.Worksheets.Add(sheetName);

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
                    worksheet.Cell(r + 2, c + 1).Value = request.Rows[r][c];
                }
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return new ServiceResult<byte[]>(ServiceStatus.Success, Data: stream.ToArray());
        }
        catch (Exception ex)
        {
            return new ServiceResult<byte[]>(ServiceStatus.NotAcceptable, Message: $"Excel dosyası oluşturulamadı: {ex.Message}");
        }
    }
}