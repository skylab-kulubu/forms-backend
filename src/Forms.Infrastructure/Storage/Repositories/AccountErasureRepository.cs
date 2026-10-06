using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class AccountErasureRepository : IAccountErasureRepository
{
    // İ/I/ı lower()'dan önce eşlenir, sonuç veritabanının yerel ayarına bağlı kalmaz.
    // SQL'deki translate ile NormalizeName aynı kuraldır; ikisi birlikte değişir.
    private const string TurkishFrom = "İIıŞĞÇÖÜÂÎÛşğçöüâîû";
    private const string TurkishTo = "iiisgcouaiusgcouaiu";

    private readonly FormsDbContext _context;

    public AccountErasureRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<List<Guid>> FindGuestResponseCandidatesAsync(IReadOnlyList<string> emails, CancellationToken ct = default) =>
        _context.Database.SqlQueryRaw<Guid>("""
            SELECT r."Id" AS "Value" FROM "Responses" r
            WHERE r."UserId" IS NULL AND EXISTS (
                SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(r."Data") = 'array' THEN r."Data" ELSE '[]' END) item
                WHERE lower(btrim(item ->> 'answer')) = ANY (@emails))
            """, new NpgsqlParameter("emails", emails.ToArray())).ToListAsync(ct);

    public Task<List<ErasureResponseRow>> GetResponsesAsync(Guid userId, IReadOnlyList<Guid> responseIds, CancellationToken ct = default) =>
        _context.Responses.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.UserId == userId || responseIds.Contains(r.Id))
            .Select(r => new ErasureResponseRow(r.Id, r.UserId, r.Form.Schema, r.Data))
            .ToListAsync(ct);

    public Task<int> RedactResponsesAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default) =>
        _context.Responses.IgnoreQueryFilters().Where(r => r.UserId == userId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.UserId, (Guid?)replacementUserId)
            .SetProperty(r => r.Data, new List<FormResponseSchemaItem>())
            .SetProperty(r => r.ReviewNote, (string?)null), ct);

    public Task<int> RedactGuestResponsesAsync(IReadOnlyList<Guid> responseIds, CancellationToken ct = default) =>
        _context.Responses.IgnoreQueryFilters().Where(r => responseIds.Contains(r.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Data, new List<FormResponseSchemaItem>())
            .SetProperty(r => r.ReviewNote, (string?)null), ct);

    // Taslak kopyası kişinin cevaplarıdır ve (form, kişi) başına tek deneme olabilir; olayları da gider.
    public Task<int> DeleteAttemptsAsync(Guid userId, CancellationToken ct = default) =>
        _context.Attempts.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);

    public async Task<int> ClearAnswerMentionsAsync(IReadOnlyList<string> emails, IReadOnlyList<string> fullNames, CancellationToken ct = default) =>
        await _context.Database.ExecuteSqlRawAsync(ClearAnswers("Responses", "Data"), MentionParameters(emails, fullNames), ct) +
        await _context.Database.ExecuteSqlRawAsync(ClearAnswers("Attempts", "DraftSnapshot"), MentionParameters(emails, fullNames), ct);

    public async Task<int> ClearNoteMentionsAsync(IReadOnlyList<string> emails, IReadOnlyList<string> fullNames, CancellationToken ct = default) =>
        await _context.Database.ExecuteSqlRawAsync(ClearNotes("Responses", "ReviewNote"), MentionParameters(emails, fullNames), ct) +
        await _context.Database.ExecuteSqlRawAsync(ClearNotes("AttemptEvents", "Note"), MentionParameters(emails, fullNames), ct);

    public async Task<int> ReplaceActorColumnsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default)
    {
        var responses = _context.Responses.IgnoreQueryFilters();
        var groups = _context.ComponentGroups.IgnoreQueryFilters();

        return await responses.Where(r => r.ReviewedBy == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedBy, (Guid?)replacementUserId), ct) +
               await responses.Where(r => r.ArchivedBy == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ArchivedBy, (Guid?)replacementUserId), ct) +
               await groups.Where(g => g.OwnedBy == userId).ExecuteUpdateAsync(s => s.SetProperty(g => g.OwnedBy, replacementUserId), ct) +
               await groups.Where(g => g.ArchivedBy == userId).ExecuteUpdateAsync(s => s.SetProperty(g => g.ArchivedBy, (Guid?)replacementUserId), ct) +
               await _context.Workflows.IgnoreQueryFilters().Where(w => w.OwnerUserId == userId)
                   .ExecuteUpdateAsync(s => s.SetProperty(w => w.OwnerUserId, replacementUserId), ct) +
               await _context.AttemptEvents.IgnoreQueryFilters().Where(e => e.ActorUserId == userId)
                   .ExecuteUpdateAsync(s => s.SetProperty(e => e.ActorUserId, (Guid?)replacementUserId), ct);
    }

    public Task<int> RemoveCollaboratorsAsync(Guid userId, CancellationToken ct = default) =>
        _context.Collaborators.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Role != CollaboratorRole.Owner)
            .ExecuteDeleteAsync(ct);

    public Task<int> ReplaceOwnersAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default) =>
        _context.Collaborators.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Role == CollaboratorRole.Owner)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, replacementUserId), ct);

    /// <summary>
    /// Kişinin aktif koşularını Terminated yapar ve açık adımlarını kapatır. FormWorkflowRuntime'ın
    /// koşuyu sonlandırırken değiştirdiği alanlar (koşuda Status, CompletedAt; adımda CompletedAt)
    /// burada da değişir; runtime'daki kural değişirse bu metot da değişmeli. Outcome None kalır:
    /// koşu onaylanmadı ya da reddedilmedi, kişi silindi.
    /// </summary>
    public async Task<int> CloseWorkflowRunsAsync(Guid userId, DateTime now, CancellationToken ct = default)
    {
        await _context.WorkflowSteps.IgnoreQueryFilters()
            .Where(s => s.CompletedAt == null && s.WorkflowInstance.UserId == userId && s.WorkflowInstance.Status == WorkflowInstanceStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAt, now).SetProperty(x => x.UpdatedAt, now), ct);

        return await _context.WorkflowInstances.IgnoreQueryFilters()
            .Where(i => i.UserId == userId && i.Status == WorkflowInstanceStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, WorkflowInstanceStatus.Terminated)
                .SetProperty(i => i.CompletedAt, now)
                .SetProperty(i => i.UpdatedAt, now), ct);
    }

    public Task<int> DetachWorkflowRunsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default) =>
        _context.WorkflowInstances.IgnoreQueryFilters().Where(i => i.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UserId, replacementUserId), ct);

    private static object[] MentionParameters(IReadOnlyList<string> emails, IReadOnlyList<string> fullNames) =>
    [
        new NpgsqlParameter("patterns", emails.Select(EmailPattern).ToArray()),
        // Tek kelime aranmaz: yalnız ad ve soyadı birlikte olan tam ad.
        new NpgsqlParameter("names", fullNames.Select(NormalizeName).Where(name => name.Contains(' ')).Distinct().ToArray()),
        new NpgsqlParameter("from", TurkishFrom),
        new NpgsqlParameter("to", TurkishTo)
    ];

    /// <summary>Metin bir adresi (adres sınırlarıyla) ya da düzleştirilmiş tam adı içeriyor mu.</summary>
    private static string Mentions(string text) => $"""
        ({text} IS NOT NULL AND (
            EXISTS (SELECT 1 FROM unnest(@patterns) p WHERE lower({text}) ~ p)
            OR EXISTS (SELECT 1 FROM unnest(@names) n
                       WHERE strpos(regexp_replace(lower(translate({text}, @from, @to)), '\s+', ' ', 'g'), n) > 0)))
        """;

    private static string ClearNotes(string table, string column) =>
        $"""UPDATE "{table}" SET "{column}" = NULL WHERE {Mentions($"\"{column}\"")}""";

    /// <summary>jsonb cevap dizisinde kişiyi anan cevapları "" yapar; değişen satır sayısını döner.</summary>
    private static string ClearAnswers(string table, string column) => $$"""
        UPDATE "{{table}}" t SET "{{column}}" = (
            SELECT jsonb_agg(CASE WHEN jsonb_typeof(e.item -> 'answer') = 'string' AND {{Mentions("e.item ->> 'answer'")}}
                                  THEN jsonb_set(e.item, ARRAY['answer'], '""') ELSE e.item END ORDER BY e.position)
            FROM jsonb_array_elements(t."{{column}}") WITH ORDINALITY AS e(item, position))
        WHERE EXISTS (
            SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(t."{{column}}") = 'array' THEN t."{{column}}" ELSE '[]' END) item
            WHERE jsonb_typeof(item -> 'answer') = 'string' AND {{Mentions("item ->> 'answer'")}})
        """;

    /// <summary>
    /// Adresin kendisi; önünde ya da arkasında adres karakteri olmamalı. Örnek: ali@x.com,
    /// "vali@x.com" ve "ali@x.com.tr" içinde bulunmaz; "&lt;ali@x.com&gt;" içinde ve cümle sonunda bulunur.
    /// </summary>
    internal static string EmailPattern(string email) =>
        "(?<![[:alnum:]._%+'-])" +
        string.Concat(email.Select(c => char.IsLetterOrDigit(c) ? c.ToString() : "\\" + c)) +
        "(?![[:alnum:]_%+'@-]|\\.[[:alnum:]])";

    internal static string NormalizeName(string name)
    {
        var mapped = new string(name.Select(c =>
        {
            var index = TurkishFrom.IndexOf(c);
            return index >= 0 ? TurkishTo[index] : c;
        }).ToArray());

        var words = mapped.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }
}
