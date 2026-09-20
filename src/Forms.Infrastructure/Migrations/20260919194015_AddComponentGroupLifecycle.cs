using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComponentGroupLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ComponentGroup",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedBy",
                table: "ComponentGroup",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComponentGroup_OwnedBy_ArchivedAt_CreatedAt",
                table: "ComponentGroup",
                columns: new[] { "OwnedBy", "ArchivedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ComponentGroup_OwnedBy_ArchivedAt_CreatedAt",
                table: "ComponentGroup");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ComponentGroup");

            migrationBuilder.DropColumn(
                name: "ArchivedBy",
                table: "ComponentGroup");
        }
    }
}
