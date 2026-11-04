using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "arrangement",
                schema: "loop",
                table: "applications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asked_pay",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "benefits",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

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
                name: "hours",
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

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "offer_terms",
                schema: "loop",
                table: "applications",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "offered_pay",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "posted_pay",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "posting",
                schema: "loop",
                table: "applications",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "posting_url",
                schema: "loop",
                table: "applications",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "questions",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "reports_to",
                schema: "loop",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "loop",
                table: "applications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_detail",
                schema: "loop",
                table: "applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "sponsors_visa",
                schema: "loop",
                table: "applications",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "stack",
                schema: "loop",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "team",
                schema: "loop",
                table: "applications",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "arrangement",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "asked_pay",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "benefits",
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
                name: "hours",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "last_raised",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_terms",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offered_pay",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "posted_pay",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "posting",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "posting_url",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "questions",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "reports_to",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source_detail",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "sponsors_visa",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "stack",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "team",
                schema: "loop",
                table: "applications");
        }
    }
}
