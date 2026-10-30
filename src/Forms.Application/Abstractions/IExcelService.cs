using Skylab.Forms.Application.Contracts.Exports;

namespace Skylab.Forms.Application.Abstractions;

public interface IExcelService
{
    byte[] GenerateExcel(ExcelExportRequest request);
}
