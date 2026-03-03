using Skylab.Api.Extensions;
using Skylab.Shared.Domain.Enums;
using Skylab.Exports.Application.Contracts;
using Skylab.Exports.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Skylab.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/internal/api/exports").WithTags("Exports");

        group.MapPost("/excel", ([FromBody] ExcelExportRequest request, IExcelService service) =>
        {
           var result = service.GenerateExcel(request);

           if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return Results.File(
                    fileContents: result.Data,
                    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileDownloadName: $"{request.SheetName ?? "export"}_{DateTime.Now:yyyyMMddHHmm}.xlsx"
                );
            }

           return result.ToApiResult(); 
        });
    }
}