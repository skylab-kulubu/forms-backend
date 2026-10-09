using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResponseGuest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "Responses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestFirstName",
                table: "Responses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestLastName",
                table: "Responses",
                type: "text",
                nullable: true);

            // Eski misafir yanıtlarında kimlik yalnızca cevap verisinde duruyor; formun
            // işaretli kimlik alanlarına (props.identity) yazılanlar yeni kolonlara taşınır.
            migrationBuilder.Sql(@"
-- jsonb_array_elements dizi olmayan değerde hata verir; CASE onu boş kümeye çevirir.
WITH identity_fields AS (
    SELECT f.""Id"" AS form_id, field->>'id' AS field_id, field->'props'->>'identity' AS identity_key
    FROM ""Forms"" AS f
    CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(f.""Schema"") = 'array' THEN f.""Schema"" END) AS s(field)
    WHERE f.""EventId"" IS NOT NULL
      AND field->'props'->>'identity' IN ('firstName', 'lastName', 'email')
),
guests AS (
    SELECT r.""Id"" AS response_id,
        max(btrim(item->>'answer')) FILTER (WHERE i.identity_key = 'firstName') AS first_name,
        max(btrim(item->>'answer')) FILTER (WHERE i.identity_key = 'lastName') AS last_name,
        max(lower(btrim(item->>'answer'))) FILTER (WHERE i.identity_key = 'email') AS email
    FROM ""Responses"" AS r
    CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(r.""Data"") = 'array' THEN r.""Data"" END) AS d(item)
    JOIN identity_fields AS i ON i.form_id = r.""FormId"" AND i.field_id = item->>'id'
    WHERE r.""UserId"" IS NULL
    GROUP BY r.""Id""
)
UPDATE ""Responses"" AS r
SET ""GuestFirstName"" = g.first_name, ""GuestLastName"" = g.last_name, ""GuestEmail"" = g.email
FROM guests AS g
WHERE r.""Id"" = g.response_id
  AND g.first_name <> '' AND g.last_name <> '' AND g.email LIKE '%@%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "GuestFirstName",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "GuestLastName",
                table: "Responses");
        }
    }
}
