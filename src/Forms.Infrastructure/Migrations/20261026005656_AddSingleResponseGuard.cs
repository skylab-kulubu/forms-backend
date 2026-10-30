using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleResponseGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSingleResponse",
                table: "Responses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Responses_SingleResponse",
                table: "Responses",
                columns: new[] { "FormId", "UserId" },
                unique: true,
                filter: "\"IsSingleResponse\" AND NOT \"IsArchived\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Responses_SingleResponse",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "IsSingleResponse",
                table: "Responses");
        }
    }
}
