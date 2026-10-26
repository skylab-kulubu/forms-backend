using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Infrastructure.Storage.Repositories;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Bağlantı yarıda koptuğunda yeniden deneme kaydı ne çiftlemeli ne de kaybetmeli.</summary>
[Collection(ApiCollection.Name)]
public class StorageRetryTests(FormsApiFactory api)
{
    /// <summary>Kurulduktan sonraki ilk komutu kopan bağlantı gibi düşürür; Npgsql bunu geçici hata sayar.</summary>
    private sealed class DropNextCommand : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            DropIfArmed();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            DropIfArmed();
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void DropIfArmed()
        {
            if (!Armed) return;
            Armed = false;
            throw new NpgsqlException("bağlantı koptu", new IOException("bağlantı koptu"));
        }
    }

    private FormsDbContext ContextWith(DropNextCommand interceptor) =>
        new(new DbContextOptionsBuilder<FormsDbContext>()
            .UseNpgsql(api.PostgresConnectionString, npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(50), null))
            .AddInterceptors(interceptor)
            .Options);

    [Fact]
    public async Task A_save_tried_again_after_a_dropped_connection_publishes_the_response_event_once()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var interceptor = new DropNextCommand();
        await using var db = ContextWith(interceptor);
        var response = new FormResponse { FormId = form.Id, UserId = Guid.NewGuid() };
        db.Responses.Add(response);

        // Transaction içindeki kayıt EF'in kendi yeniden denemesine girmez; çağıranın yeniden denediği durum budur.
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            interceptor.Armed = true;
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        }

        await db.SaveChangesAsync();

        var events = await db.Outbox.AsNoTracking().Where(message => message.Type == OutboxTypes.ResponseStatusChanged).Select(message => message.Payload).ToListAsync();
        Assert.Single(events, payload => payload.Contains(response.Id.ToString()));
    }

    [Fact]
    public async Task A_transaction_that_lost_its_connection_after_saving_is_not_retried_into_losing_that_save()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var interceptor = new DropNextCommand();
        await using var db = ContextWith(interceptor);
        var tracked = await db.Forms.SingleAsync(candidate => candidate.Id == form.Id);
        tracked.Title = "İlk kayıtta yazılan başlık";
        var uow = new FormsUnitOfWork(db);

        var failure = await Record.ExceptionAsync(() => uow.ExecuteInTransactionAsync(async token =>
        {
            await uow.SaveChangesAsync(token);

            tracked.Description = "İkinci kayıtta yazılan açıklama";
            interceptor.Armed = true;
            await uow.SaveChangesAsync(token);

            return true;
        }));

        await using var check = ContextWith(new DropNextCommand());
        var stored = await check.Forms.AsNoTracking().SingleAsync(candidate => candidate.Id == form.Id);

        // Yeniden deneme ilk kaydı kabul edilmiş sanıp yazmazdı; ikinci kayıt tek başına commit'lenirdi.
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(("Kulüp başvurusu", null), (stored.Title, stored.Description));
    }
}
