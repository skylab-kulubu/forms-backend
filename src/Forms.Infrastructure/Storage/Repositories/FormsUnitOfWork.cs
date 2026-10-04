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

        return strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);

            var result = await operation(token);

            await transaction.CommitAsync(token);

            return result;
        }, ct);
    }
}
