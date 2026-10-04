using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Intake",
                table: "Workflows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Arşivleme akışı kapatır; önceden arşivlenmiş akışların başvuruları da dursun.
            migrationBuilder.Sql(@"
-- WorkflowIntake.Closed=2, WorkflowStatus.Archived=2
UPDATE ""Workflows"" SET ""Intake"" = 2 WHERE ""Status"" = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intake",
                table: "Workflows");
        }
    }
}
