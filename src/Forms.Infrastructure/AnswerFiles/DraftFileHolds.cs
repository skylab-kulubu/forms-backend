using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.AnswerFiles;

public sealed class DraftFileHolds : IDraftFileHolds
{
    private const int Linking = (int)AnswerFileLinkState.Linking;
    private const int Unlinking = (int)AnswerFileLinkState.Unlinking;

    private readonly FormsDbContext _context;

    public DraftFileHolds(FormsDbContext context)
    {
        _context = context;
    }

    public async Task HoldAsync(Guid formId, Guid userId, IReadOnlyCollection<Guid> mediaIds, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var held = mediaIds.Distinct().ToArray();

        if (held.Length > 0)
        {
            // Kaldırılmayı bekleyen bağ yeniden istenirse geri alınır. Bağı kaldıran iş satırı yalnız
            // durumu hâlâ kaldırma iken siler, bu yüzden araya giren bu güncelleme kaybolmaz.
            await _context.Database.ExecuteSqlAsync($"""
                INSERT INTO "AnswerFileLinks" ("Id", "MediaId", "FormId", "UserId", "State", "Attempts", "NextAttemptAt", "CreatedAt")
                SELECT gen_random_uuid(), media_id, {formId}, {userId}, {Linking}, 0, {now}, {now}
                FROM unnest({held}) AS media_id
                ON CONFLICT ("FormId", "UserId", "MediaId") WHERE "ResponseId" IS NULL
                DO UPDATE SET "State" = {Linking}, "Attempts" = 0, "NextAttemptAt" = {now}
                WHERE "AnswerFileLinks"."State" = {Unlinking}
                """, ct);
        }

        await ReleaseAsync(
            _context.AnswerFileLinks.Where(l => l.FormId == formId && l.UserId == userId && l.ResponseId == null && !held.Contains(l.MediaId)),
            now,
            ct);
    }

    public Task ReleaseFormAsync(Guid formId, CancellationToken ct = default) =>
        ReleaseAsync(_context.AnswerFileLinks.Where(l => l.FormId == formId && l.ResponseId == null), DateTime.UtcNow, ct);

    private static Task<int> ReleaseAsync(IQueryable<AnswerFileLink> links, DateTime now, CancellationToken ct) =>
        links
            .Where(l => l.State != AnswerFileLinkState.Unlinking)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.State, AnswerFileLinkState.Unlinking)
                .SetProperty(l => l.Attempts, 0)
                .SetProperty(l => l.NextAttemptAt, now), ct);
}
