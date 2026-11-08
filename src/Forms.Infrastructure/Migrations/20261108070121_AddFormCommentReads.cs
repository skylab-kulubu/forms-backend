using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormCommentReads : Migration
    {
        private const string MarkExistingThreadsRead = """
            INSERT INTO "FormCommentReads" ("ThreadId", "UserId", "ReadUntil")
            SELECT DISTINCT thread."Id", member."UserId", now()
            FROM "FormComments" AS thread
            JOIN "Collaborators" AS member ON member."FormId" = thread."FormId"
            WHERE thread."ThreadId" IS NULL
            ON CONFLICT DO NOTHING;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormCommentReads",
                columns: table => new
                {
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormCommentReads", x => new { x.ThreadId, x.UserId });
                    table.ForeignKey(
                        name: "FK_FormCommentReads_FormComments_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "FormComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormCommentReads_UserId",
                table: "FormCommentReads",
                column: "UserId");

            migrationBuilder.Sql(MarkExistingThreadsRead);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormCommentReads");
        }
    }
}
