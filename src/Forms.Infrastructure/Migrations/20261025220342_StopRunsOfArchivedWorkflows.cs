using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <summary>
    /// Arşivleme eskiden yalnız akışın bayraklarını değiştiriyordu; formu doldurması beklenen başvurular sonsuza dek
    /// Active kaldı ve kişi aynı formu içeren yeni bir akışa başlayamadı. Bu başvurular, arşivlemenin artık yaptığı gibi
    /// sonuçsuz biter. İncelemeyi bekleyenler açık kalır; karar verilince orada dururlar.
    /// </summary>
    public partial class StopRunsOfArchivedWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "WorkflowSteps" AS s
                SET "CompletedAt" = now(), "UpdatedAt" = now()
                FROM "WorkflowInstances" AS i
                JOIN "Workflows" AS w ON w."Id" = i."WorkflowId"
                WHERE s."WorkflowInstanceId" = i."Id"
                  AND s."CompletedAt" IS NULL
                  AND s."ResponseId" IS NULL
                  AND i."Status" = 0
                  AND w."Status" = 2;
                """);

            migrationBuilder.Sql("""
                UPDATE "WorkflowInstances" AS i
                SET "Status" = 2, "CompletedAt" = now(), "UpdatedAt" = now()
                FROM "Workflows" AS w
                WHERE w."Id" = i."WorkflowId"
                  AND i."Status" = 0
                  AND w."Status" = 2
                  AND NOT EXISTS (SELECT 1 FROM "WorkflowSteps" AS s WHERE s."WorkflowInstanceId" = i."Id" AND s."CompletedAt" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hangi başvurunun bu migration ile durduğu artık ayırt edilemez; geri almak bir şey yapmaz.
        }
    }
}
