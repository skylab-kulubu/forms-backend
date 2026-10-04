using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PositionX",
                table: "WorkflowNodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PositionY",
                table: "WorkflowNodes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PositionX",
                table: "WorkflowNodes");

            migrationBuilder.DropColumn(
                name: "PositionY",
                table: "WorkflowNodes");
        }
    }
}
