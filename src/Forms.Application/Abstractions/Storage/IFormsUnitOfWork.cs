namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Birden fazla kayıt işlemini tek transaction'da toplar. Sağlayıcının yeniden
    /// deneme stratejisiyle uyumludur.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}
