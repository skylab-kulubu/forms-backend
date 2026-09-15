using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class ResultExtensions
{
    /// <summary>
    /// Hata yanıtları da başarı yanıtlarıyla aynı zarfı taşır. İstemci tek bir şekli
    /// çözümler ve 428 gibi durumlar yanında veri taşıyabilir.
    /// </summary>
    public static IResult ToApiResult<T>(this ServiceResult<T> result)
    {
        if (!result.Status.IsFailure())
            return Results.Ok(result);

        var (statusCode, fallback) = result.Status switch
        {
            ServiceStatus.NotFound => (404, "Kayıt bulunamadı."),
            ServiceStatus.NotAvailable => (410, "Kayıt artık mevcut değil."),
            ServiceStatus.Unauthorized => (401, "Giriş yapmalısınız."),
            ServiceStatus.NotAuthorized => (403, "Bu işlem için yetkiniz yok."),
            ServiceStatus.NotAcceptable => (400, "Veriler yanlış veya eksik."),
            ServiceStatus.RequiresParentApproval => (428, "Önceki formun onayı gereklidir."),
            ServiceStatus.ConfigurationError => (500, "Akış tanımı bu adım için sonuç üretemedi."),
            _ => (400, "Bir hata oluştu.")
        };

        var body = result.Message is null ? result with { Message = fallback } : result;

        return Results.Json(body, statusCode: statusCode);
    }

    public static IResult ToApiResult(this ServiceStatus status, string? message = null)
    {
        return new ServiceResult<object>(status, Message: message).ToApiResult();
    }
}
