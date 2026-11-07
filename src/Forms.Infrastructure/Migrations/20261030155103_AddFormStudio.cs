using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "Forms",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Forms",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "FormEdits",
                columns: table => new
                {
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ops = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormEdits", x => new { x.FormId, x.Version });
                    table.ForeignKey(
                        name: "FK_FormEdits_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormWorkingCopies",
                columns: table => new
                {
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    Document = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormWorkingCopies", x => x.FormId);
                    table.ForeignKey(
                        name: "FK_FormWorkingCopies_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormEdits_CreatedAt",
                table: "FormEdits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FormEdits_FormId_BatchId",
                table: "FormEdits",
                columns: new[] { "FormId", "BatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormEdits");

            migrationBuilder.DropTable(
                name: "FormWorkingCopies");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "Forms");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Forms");
        }
    }
}
