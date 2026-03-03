namespace Skylab.Shared.Domain.Enums;

public enum ServiceStatus
{
    Success = 200,
    Created = 201,

    NotAcceptable = 400,
    Unauthorized = 401,
    NotAuthorized = 403,
    NotFound = 404,
    NotAvailable = 410,
    
    PendingApproval = 600,
    Approved = 601,
    Declined = 602,
    RequiresParentApproval = 603
}