namespace Skylab.Forms.Application.Abstractions;

/// <summary>
/// Kalıcı olarak engellenmiş hesaplar. Yalnız bir sağlayıcı açıkça seçildiyse kayıtlıdır; kayıtlı değilse kimse
/// engelli sayılmaz, engeli ön koşul sayan iş (hesap silme) de yapılmaz.
/// </summary>
public interface IAccountAccessPolicy
{
    /// <param name="issuer">Doğrulanmış token'ın iss değeri; kişi (iss, sub) çiftiyle tanınır.</param>
    Task<AccountAccessDecision> CheckAsync(string issuer, string subject, CancellationToken ct = default);

    /// <summary>Engel listesi okunabiliyor mu; okunamıyorsa servis hazır sayılmaz.</summary>
    Task<bool> IsReadyAsync(CancellationToken ct = default);

    /// <summary>Karar okunamadığında istemciye önerilen bekleme.</summary>
    int RetryAfterSeconds { get; }
}

public enum AccountAccessDecision
{
    Allowed,
    Blocked,
    Unavailable
}
