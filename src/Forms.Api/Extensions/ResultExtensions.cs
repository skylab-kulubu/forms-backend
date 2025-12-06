using Forms.Application.Contracts;
using Microsoft.AspNetCore.Http; // IResult ve Results için gerekli

namespace Forms.API.Extensions;

public static class ResultExtensions
{
    public static IResult ToApiResult<T>(this ServiceResult<T> result)
    {
        return result.Status switch
        {
            FormAccessStatus.Available => Results.Ok(result),
            FormAccessStatus.Completed => Results.Ok(result),
            FormAccessStatus.PendingApproval => Results.Ok(result),

            FormAccessStatus.NotFound => Results.NotFound(new 
            { 
                status = result.Status, 
                message = result.Message ?? "Form bulunamadı." 
            }),

            FormAccessStatus.NotAvailable => Results.NotFound(new 
            { 
                status = result.Status, 
                message = result.Message ?? "Form artık yayında değil." 
            }),

            FormAccessStatus.NotAuthorized => Results.Json(new 
            { 
                status = result.Status, 
                message = result.Message ?? "Yetkiniz yok." 
            }, statusCode: 403),

            FormAccessStatus.RequiresParentApproval => Results.Json(new 
            { 
                status = result.Status, 
                message = result.Message ?? "Önceki onay gereklidir." 
            }, statusCode: 428),

            _ => Results.BadRequest(new 
            { 
                status = result.Status, 
                message = result.Message ?? "Bir hata oluştu." 
            })
        };
    }
}