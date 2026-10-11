using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <summary>
    /// Girişli dosyalar 2026-10-09'dan beri answer_file olarak yükleniyor ve core bağlanmayanı 24 saatte
    /// siliyor. O günden beri kaydedilen girişli cevapların dosyaları bağlanmak üzere sıraya alınır: süre
    /// dolunca kurulan geçici cevaplar hiç bağlanmamıştı, bağlanmış olanların bağ kimliği de böylece öğrenilir.
    /// Legacy dosyaları bağlama işi core'dan okuyup kendisi eler.
    /// </summary>
    public partial class AddAnswerFileLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerFileLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerFileLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerFileLinks_FormId_UserId_MediaId",
                table: "AnswerFileLinks",
                columns: new[] { "FormId", "UserId", "MediaId" },
                unique: true,
                filter: "\"ResponseId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerFileLinks_NextAttemptAt",
                table: "AnswerFileLinks",
                column: "NextAttemptAt",
                filter: "\"NextAttemptAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerFileLinks_ResponseId_MediaId",
                table: "AnswerFileLinks",
                columns: new[] { "ResponseId", "MediaId" },
                unique: true,
                filter: "\"ResponseId\" IS NOT NULL");

            migrationBuilder.Sql(@"
-- AnswerFileLinkState.Linking = 0.
INSERT INTO ""AnswerFileLinks"" (""Id"", ""MediaId"", ""FormId"", ""UserId"", ""ResponseId"", ""State"", ""Attempts"", ""NextAttemptAt"", ""CreatedAt"")
SELECT gen_random_uuid(), files.media_id, files.form_id, files.user_id, files.response_id, 0, 0, now(), now()
FROM (
    SELECT DISTINCT r.""Id"" AS response_id, r.""FormId"" AS form_id, r.""UserId"" AS user_id,
           CASE WHEN item ->> 'answer' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                THEN (item ->> 'answer')::uuid END AS media_id
    FROM ""Responses"" r
    CROSS JOIN LATERAL jsonb_array_elements(r.""Data"") AS item
    WHERE r.""UserId"" IS NOT NULL
      AND r.""SubmittedAt"" >= '2026-10-09T00:00:00Z'
      AND item ->> 'type' = 'file'
) files
WHERE files.media_id IS NOT NULL
ON CONFLICT DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerFileLinks");
        }
    }
}
