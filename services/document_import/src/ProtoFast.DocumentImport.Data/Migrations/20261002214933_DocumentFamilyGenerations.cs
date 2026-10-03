using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFamilyGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_family_generations",
                schema: "engine",
                columns: table => new
                {
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    reset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_family_generations", x => x.family);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_family_generations",
                schema: "engine");
        }
    }
}
