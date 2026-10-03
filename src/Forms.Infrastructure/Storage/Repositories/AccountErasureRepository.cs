using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skylab.Forms.Application;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class AccountErasureRepository(FormsDbContext context) : IAccountErasureRepository
{
    private const string TurkishFrom = "İIıŞĞÇÖÜÂÎÛşğçöüâîû";
    private const string TurkishTo = "iiisgcouaiusgcouaiu";

    public Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default) =>
        context.AccountErasureReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

    public async Task LockAsync(Guid requestId, CancellationToken ct = default) =>
        await context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"forms:account-erasure:" + requestId}, 0))", ct);

    public async Task AddReceiptAsync(AccountErasureReceipt receipt, CancellationToken ct = default) =>
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO account_erasure_receipts (request_id, completed_at, counts)
            VALUES ({receipt.RequestId}, {receipt.CompletedAt}, {receipt.Counts}::jsonb)
            """, ct);

    public async Task<Dictionary<string, long>> EraseAsync(AccountErasureCommand command, CancellationToken ct = default)
    {
        Guid? subject = command.SubjectId;
        Guid? deleted = DeletedUser.Id;
        var now = DateTime.UtcNow;
        var counts = new Dictionary<string, long>();

        var guestIds = command.Emails.Count == 0 ? [] : await context.Database.SqlQueryRaw<Guid>("""
            SELECT r."Id" AS "Value" FROM "Responses" r
            WHERE r."UserId" IS NULL AND EXISTS (
                SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(r."Data") = 'array' THEN r."Data" ELSE '[]' END) item
                WHERE lower(btrim(item ->> 'answer')) = ANY (@emails))
            """, new NpgsqlParameter("emails", command.Emails.ToArray())).ToListAsync(ct);

        var identities = await context.Responses.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.UserId == subject || guestIds.Contains(r.Id))
            .Select(r => new { r.Form.Schema, r.Data })
            .ToListAsync(ct);
        var names = identities
            .Select(r => EventIdentity.Extract(r.Schema, r.Data))
            .Where(identity => identity is not null)
            .Select(identity => NormalizeName($"{identity!.FirstName} {identity.LastName}"))
            .Where(name => name.Contains(' '))
            .Distinct()
            .ToArray();
        var patterns = command.Emails.Select(EmailPattern).ToArray();

        var responses = context.Responses.IgnoreQueryFilters();
        counts["responses_redacted"] = await responses.Where(r => r.UserId == subject).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.UserId, deleted)
            .SetProperty(r => r.Data, new List<FormResponseSchemaItem>())
            .SetProperty(r => r.ReviewNote, (string?)null), ct);
        counts["guest_responses_redacted"] = await responses.Where(r => guestIds.Contains(r.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Data, new List<FormResponseSchemaItem>())
            .SetProperty(r => r.ReviewNote, (string?)null), ct);

        counts["attempts_deleted"] = await context.Attempts.IgnoreQueryFilters()
            .Where(a => a.UserId == command.SubjectId).ExecuteDeleteAsync(ct);

        counts["answers_cleared"] = 0;
        counts["review_notes_cleared"] = 0;
        if (patterns.Length > 0 || names.Length > 0)
        {
            object[] Terms() =>
            [
                new NpgsqlParameter("patterns", patterns), new NpgsqlParameter("names", names),
                new NpgsqlParameter("from", TurkishFrom), new NpgsqlParameter("to", TurkishTo)
            ];
            foreach (var (table, column) in new[] { ("Responses", "Data"), ("Attempts", "DraftSnapshot") })
                counts["answers_cleared"] += (await context.Database.SqlQueryRaw<long>(ClearAnswers(table, column), Terms()).ToListAsync(ct)).Single();
            foreach (var (table, column) in new[] { ("Responses", "ReviewNote"), ("AttemptEvents", "Note") })
                counts["review_notes_cleared"] += await context.Database.ExecuteSqlRawAsync(ClearNotes(table, column), Terms(), ct);
        }

        counts["actor_columns_replaced"] =
            await responses.Where(r => r.ReviewedBy == subject).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedBy, deleted), ct) +
            await responses.Where(r => r.ArchivedBy == subject).ExecuteUpdateAsync(s => s.SetProperty(r => r.ArchivedBy, deleted), ct) +
            await context.ComponentGroups.IgnoreQueryFilters().Where(g => g.OwnedBy == command.SubjectId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.OwnedBy, DeletedUser.Id), ct) +
            await context.ComponentGroups.IgnoreQueryFilters().Where(g => g.ArchivedBy == subject)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ArchivedBy, deleted), ct) +
            await context.Workflows.IgnoreQueryFilters().Where(w => w.OwnerUserId == command.SubjectId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.OwnerUserId, DeletedUser.Id), ct) +
            await context.AttemptEvents.IgnoreQueryFilters().Where(e => e.ActorUserId == subject)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ActorUserId, deleted), ct);

        var collaborators = context.Collaborators.IgnoreQueryFilters().Where(c => c.UserId == command.SubjectId);
        counts["collaborators_deleted"] = await collaborators.Where(c => c.Role != CollaboratorRole.Owner).ExecuteDeleteAsync(ct);
        counts["form_owners_replaced"] = await collaborators.ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, DeletedUser.Id), ct);

        var runs = context.WorkflowInstances.IgnoreQueryFilters().Where(i => i.UserId == command.SubjectId);
        await context.WorkflowSteps.IgnoreQueryFilters()
            .Where(s => s.CompletedAt == null && s.WorkflowInstance.UserId == command.SubjectId && s.WorkflowInstance.Status == WorkflowInstanceStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAt, now).SetProperty(x => x.UpdatedAt, now), ct);
        counts["workflow_runs_cancelled"] = await runs.Where(i => i.Status == WorkflowInstanceStatus.Active).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, WorkflowInstanceStatus.Terminated)
            .SetProperty(i => i.CompletedAt, now)
            .SetProperty(i => i.UpdatedAt, now), ct);
        counts["workflow_runs_detached"] = await runs.ExecuteUpdateAsync(s => s.SetProperty(i => i.UserId, DeletedUser.Id), ct);

        return counts;
    }

    private static string Mentions(string text) => $"""
        ({text} IS NOT NULL AND (
            EXISTS (SELECT 1 FROM unnest(@patterns) p WHERE lower({text}) ~ p)
            OR EXISTS (SELECT 1 FROM unnest(@names) n
                       WHERE strpos(regexp_replace(lower(translate({text}, @from, @to)), '\s+', ' ', 'g'), n) > 0)))
        """;

    private static string ClearNotes(string table, string column) =>
        $"""UPDATE "{table}" SET "{column}" = NULL WHERE {Mentions($"\"{column}\"")}""";

    private static string ClearAnswers(string table, string column) => $$"""
        WITH hit AS (
            SELECT t."Id", count(*) AS answers
            FROM "{{table}}" t
            CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(t."{{column}}") = 'array' THEN t."{{column}}" ELSE '[]' END) item
            WHERE jsonb_typeof(item -> 'answer') = 'string' AND {{Mentions("item ->> 'answer'")}}
            GROUP BY t."Id"
        ), cleared AS (
            UPDATE "{{table}}" t SET "{{column}}" = (
                SELECT jsonb_agg(CASE WHEN jsonb_typeof(e.item -> 'answer') = 'string' AND {{Mentions("e.item ->> 'answer'")}}
                                      THEN jsonb_set(e.item, ARRAY['answer'], '""') ELSE e.item END ORDER BY e.position)
                FROM jsonb_array_elements(t."{{column}}") WITH ORDINALITY AS e(item, position))
            FROM hit WHERE t."Id" = hit."Id"
            RETURNING hit.answers
        )
        SELECT coalesce(sum(answers), 0)::bigint AS "Value" FROM cleared
        """;

    internal static string EmailPattern(string email) =>
        "(?<![[:alnum:]._%+'-])" +
        string.Concat(email.Select(c => char.IsLetterOrDigit(c) ? c.ToString() : "\\" + c)) +
        "(?![[:alnum:]_%+'@-]|\\.[[:alnum:]])";

    internal static string NormalizeName(string name)
    {
        var mapped = new string(name.Select(c => TurkishFrom.IndexOf(c) is var i and >= 0 ? TurkishTo[i] : c).ToArray());
        return string.Join(' ', mapped.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
