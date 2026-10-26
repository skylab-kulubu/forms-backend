using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Services;
using Skylab.Forms.Infrastructure.Scheduling;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.AnswerFiles;

/// <summary>
/// Cevap dosyalarının bağlarını core'a işler: yeni bağı kurar, kaydı gidenin bağını kaldırır ve taslağa
/// bağlı dosyanın taslakta durup durmadığına günde bir bakar, çünkü taslak Redis'te süresi dolunca haber
/// vermeden gider. Core aynı bağı yeniden istemeye var olanla cevap verdiği için yarıda kalan adım
/// güvenle yeniden denenir.
/// </summary>
public class AnswerFileLinkWorker(IServiceScopeFactory scopeFactory, IDistributedLock locks, ILogger<AnswerFileLinkWorker> logger)
    : ScheduledWorker(locks, logger)
{
    private const int BatchSize = 20;
    private static readonly TimeSpan DraftCheckInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AnswerFileLinkWorker> _logger = logger;

    protected override string LockKey => "forms:answer-file-links:lock";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    // Core yanıt vermezse bir satır iki çağrıda 30 sn, bir tur 10 dk sürer; kilit ondan uzun tutulur ki iki örnek aynı satırları işlemesin.
    protected override TimeSpan LockTtl => TimeSpan.FromMinutes(15);

    protected override bool ReleasesLock => true;

    protected override async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IAnswerFileStore>();
        var drafts = scope.ServiceProvider.GetRequiredService<IFormDraftService>();
        var now = DateTime.UtcNow;

        var due = await db.AnswerFileLinks.AsNoTracking()
            .Where(l => l.NextAttemptAt <= now)
            .OrderBy(l => l.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var link in due)
        {
            try
            {
                switch (link.State)
                {
                    case AnswerFileLinkState.Linking:
                        await LinkAsync(db, files, link, ct);
                        break;
                    case AnswerFileLinkState.Unlinking:
                        await UnlinkAsync(db, files, link, ct);
                        break;
                    default:
                        await CheckDraftAsync(db, drafts, link, ct);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Bir satırın hatası sıradakileri bekletmesin.
                _logger.LogWarning(ex, "Cevap dosyası bağı {LinkId} işlenemedi; yeniden denenecek", link.Id);
                await RetryAsync(db, link, ct);
            }
        }
    }

    private async Task LinkAsync(FormsDbContext db, IAnswerFileStore files, AnswerFileLink link, CancellationToken ct)
    {
        var read = await files.FindAsync(link.MediaId, ct);
        if (read.Outcome == FileStoreOutcome.NotFound)
        {
            await DropAsync(db, link, ct);
            return;
        }

        if (read.Outcome != FileStoreOutcome.Ok || read.File is not { } file)
        {
            await RetryAsync(db, link, ct);
            return;
        }

        // Legacy dosya eskisi gibi bağlanmaz; başkasının ya da taramada reddedilmiş dosya hiç bağlanmaz.
        if (file.Origin != FileOrigin.SignedInAnswer
            || file.UploadedBy != link.UserId
            || file.State == FileState.Rejected)
        {
            await DropAsync(db, link, ct);
            return;
        }

        var attach = await files.HoldAsync(link.MediaId, link.Holder(), link.UserId, ct);
        if (attach is { Outcome: FileStoreOutcome.Ok, HoldId: { } attachmentId })
        {
            DateTime? nextCheck = link.ResponseId is null ? DateTime.UtcNow + DraftCheckInterval : null;

            // Bu arada kaldırılmak üzere işaretlendiyse durumu korunur; kimlik yine yazılır ki bağ kaldırılabilsin.
            await db.AnswerFileLinks
                .Where(l => l.Id == link.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.AttachmentId, attachmentId)
                    .SetProperty(l => l.Attempts, l => l.State == AnswerFileLinkState.Linking ? 0 : l.Attempts)
                    .SetProperty(l => l.NextAttemptAt, l => l.State == AnswerFileLinkState.Linking ? nextCheck : l.NextAttemptAt)
                    .SetProperty(l => l.State, l => l.State == AnswerFileLinkState.Linking ? AnswerFileLinkState.Linked : l.State), ct);
            return;
        }

        if (attach.Outcome == FileStoreOutcome.NotHoldable)
        {
            if (link.ResponseId is { } responseId)
                _logger.LogWarning("Cevap {ResponseId} dosyası {MediaId} core'da cevaba bağlanamadı; core onu süresi dolunca silecek", responseId, link.MediaId);

            await DropAsync(db, link, ct);
            return;
        }

        await RetryAsync(db, link, ct);
    }

    private static async Task UnlinkAsync(FormsDbContext db, IAnswerFileStore files, AnswerFileLink link, CancellationToken ct)
    {
        if (link.AttachmentId is { } attachmentId && !await files.ReleaseAsync(link.MediaId, attachmentId, ct))
        {
            await RetryAsync(db, link, ct);
            return;
        }

        await DropAsync(db, link, ct);
    }

    private static async Task CheckDraftAsync(FormsDbContext db, IFormDraftService drafts, AnswerFileLink link, CancellationToken ct)
    {
        var row = db.AnswerFileLinks.Where(l => l.Id == link.Id && l.State == AnswerFileLinkState.Linked);

        if (link.ResponseId is null && !await drafts.HoldsFileAsync(link.FormId, link.UserId, link.MediaId, ct))
        {
            var now = DateTime.UtcNow;
            await row.ExecuteUpdateAsync(s => s
                .SetProperty(l => l.State, AnswerFileLinkState.Unlinking)
                .SetProperty(l => l.Attempts, 0)
                .SetProperty(l => l.NextAttemptAt, now), ct);
            return;
        }

        DateTime? nextCheck = link.ResponseId is null ? DateTime.UtcNow + DraftCheckInterval : null;
        await row.ExecuteUpdateAsync(s => s.SetProperty(l => l.NextAttemptAt, nextCheck), ct);
    }

    // Durum bu arada değiştiyse satıra dokunulmaz: yeni durum kendi işini sıraya koymuştur.
    private static Task DropAsync(FormsDbContext db, AnswerFileLink link, CancellationToken ct) =>
        db.AnswerFileLinks.Where(l => l.Id == link.Id && l.State == link.State).ExecuteDeleteAsync(ct);

    private static Task RetryAsync(FormsDbContext db, AnswerFileLink link, CancellationToken ct)
    {
        var attempts = link.Attempts + 1;
        var next = DateTime.UtcNow + RetryBackoff.After(attempts);

        return db.AnswerFileLinks
            .Where(l => l.Id == link.Id && l.State == link.State)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Attempts, attempts).SetProperty(l => l.NextAttemptAt, next), ct);
    }

}
