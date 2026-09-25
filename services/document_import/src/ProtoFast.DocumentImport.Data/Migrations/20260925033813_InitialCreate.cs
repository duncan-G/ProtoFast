using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engine");

            migrationBuilder.CreateTable(
                name: "document_family_executors",
                schema: "engine",
                columns: table => new
                {
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    executor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    executor_version = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_family_executors", x => new { x.family, x.executor_id, x.executor_version });
                });

            migrationBuilder.CreateTable(
                name: "document_family_verifiers",
                schema: "engine",
                columns: table => new
                {
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    verifier_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rubric = table.Column<string>(type: "text", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_family_verifiers", x => new { x.family, x.verifier_id });
                });

            migrationBuilder.CreateTable(
                name: "registry_entries",
                schema: "engine",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    promoted = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registry_entries", x => new { x.kind, x.id, x.version });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_family_executors",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "document_family_verifiers",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "registry_entries",
                schema: "engine");
        }
    }
}
