namespace Skylab.Forms.Application.Common;

public enum ServiceStatus
{
    Success = 200,
    Created = 201,

    NotAcceptable = 400,
    Unauthorized = 401,
    NotAuthorized = 403,
    NotFound = 404,
    Conflict = 409,
    NotAvailable = 410,
    ServiceUnavailable = 503,

    PendingApproval = 600,
    Approved = 601,
    Declined = 602,
    RequiresParentApproval = 603,
    Completed = 604,

    /// <summary>
    /// Yayınlanmış bir akış tanımı beklenen sonucu üretemedi. Kullanıcı hatası değil,
    /// sunucu tarafı bir tanım arızasıdır ve izlenebilir olmalıdır.
    /// </summary>
    ConfigurationError = 605
}
