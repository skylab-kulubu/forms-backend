using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    public partial class AddFormEventId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "Forms",
                type: "uuid",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Forms");
        }
    }
}
