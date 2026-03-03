using Skylab.Shared.Application.Contracts;
using Skylab.Exports.Application.Contracts;

namespace Skylab.Exports.Application.Services;

public interface IExcelService
{
    ServiceResult<byte[]> GenerateExcel(ExcelExportRequest request);
}