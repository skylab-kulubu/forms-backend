using System.Net.Http.Json;
using System.Text.Json;
using Forms.Infrastructure.Migrations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Events;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class OutboxTests(FormsApiFactory api)
{
    private sealed class ScriptedHandler(string type, bool delivers, TimeSpan keepTrying) : IOutboxHandler
    {
        public string Type => type;
        public TimeSpan KeepTrying => keepTrying;
        public Task<bool> HandleAsync(string payload, CancellationToken ct) => Task.FromResult(delivers);
    }

    [Fact]
    public async Task A_submitted_answer_and_its_review_are_published_in_forms_own_words()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var applicant = api.Users.Add("Mehmet Kaya");
        var responseId = (await (await api.ClientFor(applicant.Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = FormResponseStatus.Approved })).EnsureSuccessStatusCode();

        var published = await EventsOfAsync(responseId);

        Assert.Equal([FormResponseStatus.Pending, FormResponseStatus.Approved], published.Select(change => change.Status));
        Assert.All(published, change => Assert.Equal((form.Id, applicant.Id), (change.FormId, change.UserId!.Value)));
    }

    [Fact]
    public async Task The_dispatcher_removes_what_it_delivered_retries_the_rest_and_drops_only_what_is_too_old()
    {
        await using var host = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IOutboxHandler>(new ScriptedHandler("test.delivered", delivers: true, TimeSpan.FromDays(1)));
            services.AddSingleton<IOutboxHandler>(new ScriptedHandler("test.failing", delivers: false, TimeSpan.FromDays(1)));
            services.AddSingleton<IOutboxHandler>(new ScriptedHandler("test.stale", delivers: false, TimeSpan.FromHours(1)));
        }));

        var now = DateTime.UtcNow;
        var delivered = OutboxMessage.Of("test.delivered", new { }, now);
        var failing = OutboxMessage.Of("test.failing", new { }, now);
        var stale = OutboxMessage.Of("test.stale", new { }, now.AddHours(-2));
        // İşleyicisi olmayan tür de bekler: sağlayıcının ayarı eksik kaldıysa düzeltilince teslim edilir.
        var unknown = OutboxMessage.Of("test.unknown", new { }, now);
        var unknownTooOld = OutboxMessage.Of("test.unknown", new { }, now.AddDays(-8));

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

            // Diğer testlerin olayları da paylaşılan tabloda bekler; bir tur yalnız bu mesajları görsün.
            await db.Outbox.ExecuteDeleteAsync();
            db.Outbox.AddRange(delivered, failing, stale, unknown, unknownTooOld);
            await db.SaveChangesAsync();
        }

        var dispatcher = new OutboxDispatcher(host.Services.GetRequiredService<IServiceScopeFactory>(), host.Services.GetRequiredService<IDistributedLock>(), TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.DispatchDueAsync(CancellationToken.None);

        using var check = host.Services.CreateScope();
        var left = await check.ServiceProvider.GetRequiredService<FormsDbContext>().Outbox.AsNoTracking().ToListAsync();

        Assert.Equal(new[] { failing.Id, unknown.Id }.Order(), left.Select(message => message.Id).Order());
        Assert.All(left, message => Assert.Equal(1, message.Attempts));
        Assert.All(left, message => Assert.True(message.NextAttemptAt > now));
    }

    [Fact]
    public async Task The_migration_carries_pending_core_notifications_over_as_forms_events()
    {
        var responseId = Guid.NewGuid();
        var formId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        // Eski tablo migration'la gitti; dönüşümü sınamak için aynı biçimde yeniden kurulur.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "ResponseNotifications" ("Id" uuid PRIMARY KEY, "FormId" uuid NOT NULL, "Payload" jsonb NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL, "Attempts" integer NOT NULL, "NextAttemptAt" timestamp with time zone NOT NULL)
            """);
        try
        {
            var payload = JsonSerializer.Serialize(new { responseId, status = "accepted", userId, guest = new { firstName = "Ayşe", lastName = "Yılmaz", email = "ayse@example.com" } });
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "ResponseNotifications" VALUES ({0}, {1}, {2}::jsonb, now(), 3, now())""",
                notificationId, formId, payload);

            await db.Database.ExecuteSqlRawAsync(MoveResponseNotificationsToOutbox.CopyPendingNotifications);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE "ResponseNotifications" """);
        }

        var message = await db.Outbox.AsNoTracking().SingleAsync(m => m.Id == notificationId);
        var change = JsonSerializer.Deserialize<ResponseStatusChanged>(message.Payload, OutboxMessage.Json)!;

        Assert.Equal((OutboxTypes.ResponseStatusChanged, 3), (message.Type, message.Attempts));
        Assert.Equal((responseId, formId, FormResponseStatus.Approved, userId), (change.ResponseId, change.FormId, change.Status, change.UserId!.Value));
        Assert.Equal(("Ayşe", "Yılmaz", "ayse@example.com"), (change.Guest?.FirstName, change.Guest?.LastName, change.Guest?.Email));
    }

    private async Task<List<ResponseStatusChanged>> EventsOfAsync(Guid responseId)
    {
        using var scope = api.Services.CreateScope();
        var messages = await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Outbox.AsNoTracking()
            .Where(message => message.Type == OutboxTypes.ResponseStatusChanged)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();

        return messages
            .Select(message => JsonSerializer.Deserialize<ResponseStatusChanged>(message.Payload, OutboxMessage.Json)!)
            .Where(change => change.ResponseId == responseId)
            .ToList();
    }
}
