using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <summary>
    /// Düzeltme istenen adımın gönderim numarası ve işaretlenen sorular. Var olan adımların hepsi
    /// ilk gönderimdir.
    /// </summary>
    public partial class AddWorkflowRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "WorkflowSteps",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<List<string>>(
                name: "ReturnedQuestionIds",
                table: "Responses",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Round",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "ReturnedQuestionIds",
                table: "Responses");
        }
    }
}
