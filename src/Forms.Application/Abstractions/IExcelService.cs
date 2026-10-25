using Skylab.Forms.Application.Contracts.Exports;

namespace Skylab.Forms.Application.Abstractions;

public interface IExcelService
{
    /// <summary>Sayfa adını Excel'in kurallarına kendisi uydurur; beklenmeyen bir hata istemci hatası sayılmaz, yükselir.</summary>
    byte[] GenerateExcel(ExcelExportRequest request);
}
