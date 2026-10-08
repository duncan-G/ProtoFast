using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovedSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "removal_reason",
                schema: "engine",
                table: "document_family_skills",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                schema: "engine",
                table: "document_family_skills",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "removal_reason",
                schema: "engine",
                table: "document_family_skills");

            migrationBuilder.DropColumn(
                name: "removed_at",
                schema: "engine",
                table: "document_family_skills");
        }
    }
}
