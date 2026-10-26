using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormsUnitOfWork : IFormsUnitOfWork
{
    private readonly FormsDbContext _context;

    public FormsUnitOfWork(FormsDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.ChangeTracker.Clear();
            throw new StorageConflictException(ex);
        }
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        // Bağlantı yeniden deneme ile yapılandırıldığı için kullanıcı tarafından
        // başlatılan transaction execution strategy üzerinden açılmak zorunda.
        var strategy = _context.Database.CreateExecutionStrategy();
        Exception? unsafeToRetry = null;

        return strategy.ExecuteAsync(async token =>
        {
            if (unsafeToRetry is not null)
                throw new InvalidOperationException("Transaction kaydettiği değişikliklerden sonra düştü; güvenle yeniden denenemez.", unsafeToRetry);

            var saves = 0;
            void Count(object? sender, SavedChangesEventArgs e) => saves++;
            _context.SavedChanges += Count;

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(token);

                var result = await operation(token);

                await transaction.CommitAsync(token);

                return result;
            }
            catch (Exception ex) when (saves > 0)
            {
                unsafeToRetry = ex;
                throw;
            }
            finally
            {
                _context.SavedChanges -= Count;
            }
        }, ct);
    }
}
