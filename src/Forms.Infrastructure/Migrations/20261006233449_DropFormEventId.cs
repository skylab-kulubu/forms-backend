using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropFormEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Forms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "Forms",
                type: "uuid",
                nullable: true);
        }
    }
}
