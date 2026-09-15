namespace Skylab.Forms.Application.Common;

public static class ServiceStatusExtensions
{
    /// <summary>
    /// Çağrının başarısız olup olmadığı. 600'lü blok akış durumlarını taşır ve
    /// çoğu başarılıdır; tek istisna önceki adımın beklenmesidir.
    /// </summary>
    public static bool IsFailure(this ServiceStatus status) => status switch
    {
        ServiceStatus.Success or ServiceStatus.Created => false,
        ServiceStatus.PendingApproval or ServiceStatus.Approved => false,
        ServiceStatus.Declined or ServiceStatus.Completed => false,
        _ => true
    };
}
