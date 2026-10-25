using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    public partial class MoveResponseNotificationsToOutbox : Migration
    {
        public const string CopyPendingNotifications = """
            INSERT INTO "OutboxMessages" ("Id", "Type", "Payload", "CreatedAt", "Attempts", "NextAttemptAt")
            SELECT "Id", 'response.status-changed',
                   jsonb_build_object(
                       'responseId', "Payload" -> 'responseId',
                       'formId', to_jsonb("FormId"),
                       'status', CASE "Payload" ->> 'status' WHEN 'accepted' THEN 'Approved' WHEN 'pending' THEN 'Pending' ELSE 'Declined' END,
                       'userId', "Payload" -> 'userId',
                       'guest', "Payload" -> 'guest'),
                   "CreatedAt", "Attempts", "NextAttemptAt"
            FROM "ResponseNotifications";
            """;

        private const string CopyBackPendingEvents = """
            INSERT INTO "ResponseNotifications" ("Id", "FormId", "Payload", "CreatedAt", "Attempts", "NextAttemptAt")
            SELECT "Id", ("Payload" ->> 'formId')::uuid,
                   jsonb_build_object(
                       'responseId', "Payload" -> 'responseId',
                       'status', CASE WHEN "Payload" ->> 'status' IN ('NonRestrict', 'Approved') THEN 'accepted'
                                      WHEN "Payload" ->> 'status' IN ('Pending', 'Flagged') THEN 'pending'
                                      ELSE 'declined' END,
                       'userId', "Payload" -> 'userId',
                       'guest', CASE WHEN coalesce("Payload" -> 'guest' ->> 'firstName', '') <> '' AND coalesce("Payload" -> 'guest' ->> 'lastName', '') <> ''
                                     THEN jsonb_build_object('firstName', "Payload" -> 'guest' -> 'firstName', 'lastName', "Payload" -> 'guest' -> 'lastName', 'email', "Payload" -> 'guest' -> 'email')
                                     ELSE 'null'::jsonb END),
                   "CreatedAt", "Attempts", "NextAttemptAt"
            FROM "OutboxMessages"
            WHERE "Type" = 'response.status-changed'
              AND "Payload" ->> 'status' <> 'Provisional'
              AND coalesce("Payload" ->> 'userId', '') <> '00000000-0000-4000-8000-000000000000';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_NextAttemptAt",
                table: "OutboxMessages",
                column: "NextAttemptAt");

            migrationBuilder.Sql(CopyPendingNotifications);

            migrationBuilder.DropTable(
                name: "ResponseNotifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResponseNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponseNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResponseNotifications_NextAttemptAt",
                table: "ResponseNotifications",
                column: "NextAttemptAt");

            migrationBuilder.Sql(CopyBackPendingEvents);

            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
