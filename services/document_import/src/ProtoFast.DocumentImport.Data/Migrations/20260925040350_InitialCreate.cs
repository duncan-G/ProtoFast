using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                name: "document_family_policies",
                schema: "engine",
                columns: table => new
                {
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    workflow_version = table.Column<int>(type: "integer", nullable: true),
                    confidence_alpha = table.Column<double>(type: "double precision", nullable: false),
                    confidence_beta = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_family_policies", x => x.family);
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
                name: "mined_workflow_drafts",
                schema: "engine",
                columns: table => new
                {
                    workflow_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    workflow_version = table.Column<int>(type: "integer", nullable: false),
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mined = table.Column<string>(type: "jsonb", nullable: false),
                    mined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mined_workflow_drafts", x => new { x.workflow_id, x.workflow_version });
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

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "engine",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    facets = table.Column<string>(type: "jsonb", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trace_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "stage_policies",
                schema: "engine",
                columns: table => new
                {
                    family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ladder = table.Column<string>(type: "jsonb", nullable: false),
                    primary = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confidence_alpha = table.Column<double>(type: "double precision", nullable: false),
                    confidence_beta = table.Column<double>(type: "double precision", nullable: false),
                    shadow = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    shadow_alpha = table.Column<double>(type: "double precision", nullable: false),
                    shadow_beta = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_policies", x => new { x.family, x.stage_id });
                });

            migrationBuilder.CreateTable(
                name: "run_decisions",
                schema: "engine",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    choice = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_decisions", x => x.sequence);
                    table.ForeignKey(
                        name: "fk_run_decisions_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "engine",
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stage_records",
                schema: "engine",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    record = table.Column<string>(type: "jsonb", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_records", x => x.sequence);
                    table.ForeignKey(
                        name: "fk_stage_records_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "engine",
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_run_decisions_run_id_sequence",
                schema: "engine",
                table: "run_decisions",
                columns: new[] { "run_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_family_mode_closed_at",
                schema: "engine",
                table: "runs",
                columns: new[] { "family", "mode", "closed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stage_records_run_id_sequence",
                schema: "engine",
                table: "stage_records",
                columns: new[] { "run_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_family_executors",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "document_family_policies",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "document_family_verifiers",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "mined_workflow_drafts",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "registry_entries",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "run_decisions",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "stage_policies",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "stage_records",
                schema: "engine");

            migrationBuilder.DropTable(
                name: "runs",
                schema: "engine");
        }
    }
}
