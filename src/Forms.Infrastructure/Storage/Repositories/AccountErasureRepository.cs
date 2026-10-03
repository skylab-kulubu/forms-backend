using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

/// <summary>
/// Hesap silmenin veritabanı tarafı. Sorgu filtreleri (silinmiş form, arşivlenmiş
/// şablon) burada geçerli değildir: kişinin verisi her satırda silinir. Kişinin kimliği,
/// adresleri ve adı yalnız parametre olarak gider; SQL metnine ya da loga girmez.
/// </summary>
public sealed class AccountErasureRepository(FormsDbContext context) : IAccountErasureRepository
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Misafir yanıtı (UserId boş), cevaplarından biri komuttaki bir adresin kendisiyse
    // kişinindir: büyük/küçük harf ve baştaki/sondaki boşluk fark etmez.
    private const string GuestResponseOfSubject = """
        r."UserId" IS NULL
        AND cardinality(@emails) > 0
        AND EXISTS (
            SELECT 1
            FROM jsonb_array_elements(CASE WHEN jsonb_typeof(r."Data") = 'array' THEN r."Data" ELSE '[]'::jsonb END) item(answer)
            WHERE jsonb_typeof(item.answer -> 'answer') = 'string'
              AND lower(btrim(item.answer ->> 'answer')) = ANY (@emails))
        """;

    // Bir metin, kişinin adreslerinden ya da tam adlarından birini içeriyor mu.
    private static string MentionsSubject(string text) => $"""
        EXISTS (SELECT 1 FROM unnest(@terms) term WHERE strpos(lower({text}), lower(term)) > 0)
        """;

    // Bir jsonb cevap dizisinde kişiyi anan cevapları boşaltır, kaç cevap boşaldığını döner.
    private static string ClearMentioningAnswers(string table, string column) => $$"""
        WITH hit AS (
            SELECT t."Id", count(*) AS answers
            FROM "{{table}}" t
            CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(t."{{column}}") = 'array' THEN t."{{column}}" ELSE '[]'::jsonb END) item(answer)
            WHERE jsonb_typeof(item.answer -> 'answer') = 'string'
              AND {{MentionsSubject("item.answer ->> 'answer'")}}
            GROUP BY t."Id"
        ), cleared AS (
            UPDATE "{{table}}" t
            SET "{{column}}" = (
                SELECT jsonb_agg(
                    CASE WHEN jsonb_typeof(item.answer -> 'answer') = 'string'
                          AND {{MentionsSubject("item.answer ->> 'answer'")}}
                         THEN jsonb_set(item.answer, '{answer}', '""'::jsonb)
                         ELSE item.answer END
                    ORDER BY item.position)
                FROM jsonb_array_elements(t."{{column}}") WITH ORDINALITY AS item(answer, position))
            FROM hit
            WHERE t."Id" = hit."Id"
            RETURNING hit.answers
        )
        SELECT coalesce(sum(answers), 0)::bigint FROM cleared
        """;

    public async Task<AccountErasureReceiptContract?> FindReceiptAsync(Guid requestId, CancellationToken ct = default)
    {
        var receipt = await context.AccountErasureReceipts.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

        return receipt is null ? null : ToContract(receipt);
    }

    public async Task<IReadOnlyList<AccountErasureSubjectResponse>> GetSubjectResponsesAsync(
        AccountErasureCommand command,
        CancellationToken ct = default)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var select = new NpgsqlCommand($"""
                SELECT r."Id", f."Schema"::text, r."Data"::text
                FROM "Responses" r
                JOIN "Forms" f ON f."Id" = r."FormId"
                WHERE r."UserId" = @subject OR ({GuestResponseOfSubject})
                """, connection);
            AddSubjectParameters(select, command, []);

            var responses = new List<AccountErasureSubjectResponse>();
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                responses.Add(new AccountErasureSubjectResponse(
                    reader.GetGuid(0),
                    Deserialize<List<FormSchemaItem>>(reader.GetString(1)),
                    Deserialize<List<FormResponseSchemaItem>>(reader.GetString(2))));
            }
            return responses;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public Task<AccountErasureReceiptContract> EraseAsync(AccountErasureWork work, CancellationToken ct = default)
    {
        // Bağlantı yeniden deneme ile yapılandırıldığı için transaction execution
        // strategy içinde açılır. Yeniden deneme bütün işi baştan yapar: kilit ve
        // makbuz kontrolü sayesinde iş iki kez yapılmaz.
        var strategy = context.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

            // Aynı komutun eşzamanlı iki çağrısından ikincisi burada bekler, sonra
            // ilkinin makbuzunu bulur.
            await ExecuteAsync(connection, dbTransaction, """
                SELECT pg_advisory_xact_lock(hashtextextended('forms:account-erasure:' || @request_id::text, 0))
                """, command => command.Parameters.Add(new NpgsqlParameter("request_id", NpgsqlDbType.Uuid) { Value = work.Command.RequestId }), token);

            var existing = await context.AccountErasureReceipts.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequestId == work.Command.RequestId, token);
            if (existing is not null)
                return ToContract(existing);

            var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var (key, value) in work.CacheCounts)
                counts[key] = value;

            var terms = work.Command.Emails.Concat(work.Names).ToArray();

            async Task Run(string key, string sql)
            {
                var changed = await ExecuteAsync(connection, dbTransaction, sql,
                    command => AddSubjectParameters(command, work.Command, terms), token);
                counts[key] = counts.GetValueOrDefault(key) + changed;
            }

            // Kişinin kendi yanıtları: kayıt, form, tarih ve durum kalır; cevaplar ve
            // inceleme notu silinir, yanıtlayan Silinmiş kullanıcı olur.
            await Run("responses_redacted", """
                UPDATE "Responses" SET "UserId" = @deleted, "Data" = '[]'::jsonb, "ReviewNote" = NULL
                WHERE "UserId" = @subject
                """);

            await Run("guest_responses_redacted", $"""
                UPDATE "Responses" r SET "Data" = '[]'::jsonb, "ReviewNote" = NULL
                WHERE {GuestResponseOfSubject}
                """);

            // Kişinin süreli form denemeleri silinir: taslak kopyası kişinin cevaplarıdır ve
            // (form, kişi) başına tek deneme kuralı yer tutucuya geçirmeye izin vermez.
            // Yanıtın kendisi kalır; olaylar denemeyle birlikte gider.
            await Run("attempts_deleted", """
                DELETE FROM "Attempts" WHERE "UserId" = @subject
                """);

            // Başkalarının yanıtlarında ve deneme taslaklarında kişiyi anan cevap
            // bütünüyle silinir; öbür cevaplar kalır. Sayı, silinen cevap sayısıdır.
            if (terms.Length > 0)
            {
                counts["answers_cleared"] =
                    await ScalarAsync(connection, dbTransaction, ClearMentioningAnswers("Responses", "Data"),
                        command => AddSubjectParameters(command, work.Command, terms), token) +
                    await ScalarAsync(connection, dbTransaction, ClearMentioningAnswers("Attempts", "DraftSnapshot"),
                        command => AddSubjectParameters(command, work.Command, terms), token);

                await Run("review_notes_cleared", $"""
                    UPDATE "Responses" SET "ReviewNote" = NULL
                    WHERE "ReviewNote" IS NOT NULL AND {MentionsSubject("\"ReviewNote\"")}
                    """);
                await Run("review_notes_cleared", $"""
                    UPDATE "AttemptEvents" SET "Note" = NULL
                    WHERE "Note" IS NOT NULL AND {MentionsSubject("\"Note\"")}
                    """);
            }
            else
            {
                counts["answers_cleared"] = 0;
                counts["review_notes_cleared"] = 0;
            }

            await Run("actor_columns_replaced", """
                UPDATE "Responses" SET "ReviewedBy" = @deleted WHERE "ReviewedBy" = @subject
                """);
            await Run("actor_columns_replaced", """
                UPDATE "Responses" SET "ArchivedBy" = @deleted WHERE "ArchivedBy" = @subject
                """);
            await Run("actor_columns_replaced", """
                UPDATE "ComponentGroup" SET "OwnedBy" = @deleted WHERE "OwnedBy" = @subject
                """);
            await Run("actor_columns_replaced", """
                UPDATE "ComponentGroup" SET "ArchivedBy" = @deleted WHERE "ArchivedBy" = @subject
                """);
            await Run("actor_columns_replaced", """
                UPDATE "Workflows" SET "OwnerUserId" = @deleted WHERE "OwnerUserId" = @subject
                """);
            await Run("actor_columns_replaced", """
                UPDATE "AttemptEvents" SET "ActorUserId" = @deleted WHERE "ActorUserId" = @subject
                """);

            // Ortak çalışma satırları silinir. Sahiplik satırı kalır ve Silinmiş kullanıcıya
            // geçer: her formun tek sahibi olur, kimse kendiliğinden sahip yapılmaz.
            // Yer tutucu aynı formda başka rolle duruyorsa (elle eklenmiş) birincil anahtar
            // çakışmasın diye o satır önce gider.
            await ExecuteAsync(connection, dbTransaction, """
                DELETE FROM "Collaborators" placeholder
                WHERE placeholder."UserId" = @deleted AND placeholder."Role" <> @owner
                  AND EXISTS (SELECT 1 FROM "Collaborators" c
                              WHERE c."FormId" = placeholder."FormId" AND c."UserId" = @subject AND c."Role" = @owner)
                """, command => AddSubjectParameters(command, work.Command, terms), token);
            await Run("collaborators_deleted", """
                DELETE FROM "Collaborators" WHERE "UserId" = @subject AND "Role" <> @owner
                """);
            await Run("form_owners_replaced", """
                UPDATE "Collaborators" SET "UserId" = @deleted WHERE "UserId" = @subject AND "Role" = @owner
                """);

            // Açık başvuru iptal edilir: açık adım kapanır, böylece incelemesi rota
            // üretemez. Ardından bütün başvurular yer tutucuya geçer; yer tutucunun aktif
            // başvurusu olmadığı için akış başına tek aktif başvuru kuralı bozulmaz.
            await ExecuteAsync(connection, dbTransaction, """
                UPDATE "WorkflowSteps" s SET "CompletedAt" = @now, "UpdatedAt" = @now
                FROM "WorkflowInstances" i
                WHERE s."WorkflowInstanceId" = i."Id" AND i."UserId" = @subject
                  AND i."Status" = @active AND s."CompletedAt" IS NULL
                """, command => AddSubjectParameters(command, work.Command, terms), token);
            await Run("workflow_runs_cancelled", """
                UPDATE "WorkflowInstances" SET "Status" = @terminated, "CompletedAt" = @now, "UpdatedAt" = @now
                WHERE "UserId" = @subject AND "Status" = @active
                """);
            await Run("workflow_runs_detached", """
                UPDATE "WorkflowInstances" SET "UserId" = @deleted WHERE "UserId" = @subject
                """);

            var receipt = new AccountErasureReceipt
            {
                RequestId = work.Command.RequestId,
                CompletedAt = TruncateToMicroseconds(DateTime.UtcNow),
                Counts = JsonSerializer.Serialize(counts)
            };
            await ExecuteAsync(connection, dbTransaction, """
                INSERT INTO account_erasure_receipts (request_id, completed_at, counts)
                VALUES (@request_id, @completed_at, @counts::jsonb)
                """, command =>
                {
                    command.Parameters.Add(new NpgsqlParameter("request_id", NpgsqlDbType.Uuid) { Value = receipt.RequestId });
                    command.Parameters.Add(new NpgsqlParameter("completed_at", NpgsqlDbType.TimestampTz) { Value = receipt.CompletedAt });
                    command.Parameters.Add(new NpgsqlParameter("counts", NpgsqlDbType.Text) { Value = receipt.Counts });
                }, token);

            await transaction.CommitAsync(token);

            return ToContract(receipt);
        }, ct);
    }

    private static void AddSubjectParameters(NpgsqlCommand command, AccountErasureCommand erasure, string[] terms)
    {
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = erasure.SubjectId });
        command.Parameters.Add(new NpgsqlParameter("deleted", NpgsqlDbType.Uuid) { Value = DeletedUser.Id });
        command.Parameters.Add(new NpgsqlParameter("emails", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = erasure.Emails.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("terms", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = terms });
        command.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Integer) { Value = (int)CollaboratorRole.Owner });
        command.Parameters.Add(new NpgsqlParameter("active", NpgsqlDbType.Integer) { Value = (int)WorkflowInstanceStatus.Active });
        command.Parameters.Add(new NpgsqlParameter("terminated", NpgsqlDbType.Integer) { Value = (int)WorkflowInstanceStatus.Terminated });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = DateTime.UtcNow });
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Action<NpgsqlCommand> parameters,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        parameters(command);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Action<NpgsqlCommand> parameters,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        parameters(command);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static T Deserialize<T>(string json) where T : new() =>
        JsonSerializer.Deserialize<T>(json, CamelCase) ?? new T();

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

    private static AccountErasureReceiptContract ToContract(AccountErasureReceipt receipt) =>
        new(
            receipt.RequestId,
            DateTime.SpecifyKind(receipt.CompletedAt, DateTimeKind.Utc),
            new SortedDictionary<string, long>(
                JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.Counts) ?? [],
                StringComparer.Ordinal));
}
