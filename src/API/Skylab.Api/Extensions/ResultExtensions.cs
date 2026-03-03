using Skylab.Shared.Domain.Enums;
using Skylab.Shared.Application.Contracts;

namespace Skylab.Api.Extensions;

public static class ResultExtensions
{
    public static IResult ToApiResult<T>(this ServiceResult<T> result)
    {
        return result.Status switch
        {
            ServiceStatus.Success => Results.Ok(result),
            ServiceStatus.Created => Results.Ok(result),
            ServiceStatus.Approved => Results.Ok(result),
            ServiceStatus.Declined => Results.Ok(result),
            ServiceStatus.PendingApproval => Results.Ok(result),

            ServiceStatus.NotFound => Results.NotFound(new
            {
                status = result.Status,
                message = result.Message ?? "Kayıt bulunamadı."
            }),

            ServiceStatus.NotAvailable => Results.NotFound(new
            {
                status = result.Status,
                message = result.Message ?? "Kayıt artık mevcut değil."
            }),

            ServiceStatus.Unauthorized => Results.Json(new
            {
                status = result.Status,
                message = result.Message ?? "Giriş yapmalısınız."
            }, statusCode: 401),

            ServiceStatus.NotAuthorized => Results.Json(new
            {
                status = result.Status,
                message = result.Message ?? "Bu işlem için yetkiniz yok."
            }, statusCode: 403),

            ServiceStatus.NotAcceptable => Results.Json(new
            {
                status = result.Status,
                message = result.Message ?? "Veriler yanlış veya eksik."
            }, statusCode: 400),

            ServiceStatus.RequiresParentApproval => Results.Json(new
            {
                status = result.Status,
                message = result.Message ?? "Önceki formun onayı gereklidir."
            }, statusCode: 428),

            _ => Results.BadRequest(new
            {
                status = result.Status,
                message = result.Message ?? "Bir hata oluştu."
            })
        };
    }

    public static IResult ToApiResult(this ServiceStatus status, string? message = null)
    {
        return new ServiceResult<object>(status, Message: message).ToApiResult();
    }
}