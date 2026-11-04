using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class CompanyNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "loop",
                table: "notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notes_company_id",
                schema: "loop",
                table: "notes",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "fk_notes_companies_company_id",
                schema: "loop",
                table: "notes",
                column: "company_id",
                principalSchema: "loop",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // A round note moves to the company its title starts with, by the company's first word
            // at least ("Juniper pairing session" is Juniper Ledger's); one naming none becomes a story.
            migrationBuilder.Sql("""
                UPDATE loop.notes n
                SET company_id = (
                    SELECT c.id
                    FROM loop.companies c
                    WHERE c.user_id = n.user_id
                        AND starts_with(lower(n.title), lower(split_part(c.name, ' ', 1)))
                    ORDER BY starts_with(lower(n.title), lower(c.name)) DESC, length(c.name) DESC, c.id
                    LIMIT 1)
                WHERE n.kind = 'Round';

                UPDATE loop.notes
                SET kind = CASE WHEN company_id IS NULL THEN 'Story' ELSE 'Company' END
                WHERE kind = 'Round';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE loop.notes SET kind = 'Round' WHERE kind = 'Company';");

            migrationBuilder.DropForeignKey(
                name: "fk_notes_companies_company_id",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "ix_notes_company_id",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "loop",
                table: "notes");
        }
    }
}
