using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountErasureReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_erasure_receipts",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_erasure_receipts", x => x.request_id);
                    table.CheckConstraint("account_erasure_receipts_counts_object", "jsonb_typeof(counts) = 'object'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_erasure_receipts");
        }
    }
}
