using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeManualReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresManualReview",
                table: "WorkflowNodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Arşivlenmiş sürümler de doldurulur: devam eden başvurular onları okur.
            migrationBuilder.Sql(@"
UPDATE ""WorkflowNodes"" n
SET ""RequiresManualReview"" = f.""RequiresManualReview""
FROM ""Forms"" f
WHERE f.""Id"" = n.""FormId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresManualReview",
                table: "WorkflowNodes");
        }
    }
}
