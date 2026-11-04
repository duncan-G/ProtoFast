using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class Companies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    about = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    revenue = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    growth = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    headcount = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    funding = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_raised = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    values = table.Column<List<string>>(type: "text[]", nullable: false),
                    practices = table.Column<List<string>>(type: "text[]", nullable: false),
                    competitors = table.Column<List<string>>(type: "text[]", nullable: false),
                    concerns = table.Column<List<string>>(type: "text[]", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    contacts = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_user_id",
                schema: "loop",
                table: "companies",
                column: "user_id");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "loop",
                table: "applications",
                type: "uuid",
                nullable: true);

            // One company per user and name, ignoring case and outer spaces: the facts from its most
            // recently modified application, the people from all of them.
            migrationBuilder.Sql("""
                WITH latest AS (
                    SELECT DISTINCT ON (user_id, lower(trim(company)))
                        user_id, lower(trim(company)) AS key, trim(company) AS name, about, revenue,
                        growth, headcount, funding, last_raised, "values", practices, competitors,
                        concerns, date_last_modified
                    FROM loop.applications
                    ORDER BY user_id, lower(trim(company)), date_last_modified DESC, id
                ),
                firsts AS (
                    SELECT user_id, lower(trim(company)) AS key, min(date_created) AS date_created
                    FROM loop.applications
                    GROUP BY user_id, lower(trim(company))
                ),
                people AS (
                    SELECT a.user_id, lower(trim(a.company)) AS key,
                        jsonb_agg(contact.value ORDER BY a.date_created, a.id, contact.ordinality) AS contacts
                    FROM loop.applications a
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE WHEN jsonb_typeof(a.contacts) = 'array' THEN a.contacts ELSE '[]' END
                    ) WITH ORDINALITY AS contact(value, ordinality)
                    GROUP BY a.user_id, lower(trim(a.company))
                )
                INSERT INTO loop.companies (
                    id, user_id, name, about, revenue, growth, headcount, funding, last_raised,
                    "values", practices, competitors, concerns, contacts, date_created, date_last_modified)
                SELECT gen_random_uuid(), l.user_id, l.name, l.about, l.revenue, l.growth, l.headcount,
                    l.funding, l.last_raised, l."values", l.practices, l.competitors, l.concerns,
                    coalesce(p.contacts, '[]'), f.date_created, l.date_last_modified
                FROM latest l
                JOIN firsts f USING (user_id, key)
                LEFT JOIN people p USING (user_id, key);

                UPDATE loop.applications a
                SET company_id = c.id
                FROM loop.companies c
                WHERE c.user_id = a.user_id AND lower(c.name) = lower(trim(a.company));
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "company_id",
                schema: "loop",
                table: "applications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_company_id",
                schema: "loop",
                table: "applications",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_companies_company_id",
                schema: "loop",
                table: "applications",
                column: "company_id",
                principalSchema: "loop",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "about",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "company",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "competitors",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "concerns",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "contacts",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "funding",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "growth",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "headcount",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "last_raised",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "practices",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "revenue",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "values",
                schema: "loop",
                table: "applications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_companies_company_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.AddColumn<string>(
                name: "about",
                schema: "loop",
                table: "applications",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "company",
                schema: "loop",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "competitors",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "concerns",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "contacts",
                schema: "loop",
                table: "applications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "funding",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "growth",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "headcount",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "last_raised",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "practices",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "revenue",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "values",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Every application of a company gets all of its people.
            migrationBuilder.Sql("""
                UPDATE loop.applications a
                SET company = c.name, about = c.about, revenue = c.revenue, growth = c.growth,
                    headcount = c.headcount, funding = c.funding, last_raised = c.last_raised,
                    "values" = c."values", practices = c.practices, competitors = c.competitors,
                    concerns = c.concerns, contacts = coalesce(c.contacts, '[]')
                FROM loop.companies c
                WHERE c.id = a.company_id;
                """);

            migrationBuilder.DropIndex(
                name: "ix_applications_company_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "loop");
        }
    }
}
