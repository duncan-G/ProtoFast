using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProtoFast.Segmentation.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_instincts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    guidance = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    confirmations = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    promoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_instincts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_calls",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    limit_pool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sensitivity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cached_input_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    batched = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_calls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "qualifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    model_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    qualified = table.Column<bool>(type: "boolean", nullable: false),
                    metrics_json = table.Column<string>(type: "jsonb", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qualifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_tasks",
                columns: table => new
                {
                    review_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    workflow_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    findings_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    decided_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    edits_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_tasks", x => x.review_id);
                });

            migrationBuilder.CreateTable(
                name: "run_results",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tree_json = table.Column<string>(type: "jsonb", nullable: false),
                    paragraphs_json = table.Column<string>(type: "jsonb", nullable: false),
                    augmentations_json = table.Column<string>(type: "jsonb", nullable: false),
                    tree_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_results", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sensitivity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    condition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    upload_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    augmentations = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    pinned_models = table.Column<string>(type: "jsonb", nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    tree_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "uploads",
                columns: table => new
                {
                    upload_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    with_layout = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uploads", x => x.upload_id);
                });

            migrationBuilder.CreateTable(
                name: "run_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_run_events_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_phases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    artifact_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    repair_rounds = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_phases", x => x.id);
                    table.ForeignKey(
                        name: "fk_run_phases_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_family_instincts_family_pattern",
                table: "family_instincts",
                columns: new[] { "family", "pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_calls_provider_at",
                table: "model_calls",
                columns: new[] { "provider", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_model_calls_run_id",
                table: "model_calls",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_qualifications_model_key_role_prompt_version",
                table: "qualifications",
                columns: new[] { "model_key", "role", "prompt_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_tasks_run_id",
                table: "review_tasks",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_tasks_status_created_at",
                table: "review_tasks",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_run_events_run_id_id",
                table: "run_events",
                columns: new[] { "run_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_run_phases_run_id_phase",
                table: "run_phases",
                columns: new[] { "run_id", "phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_run_results_owner_subject_document_id",
                table: "run_results",
                columns: new[] { "owner_subject", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_owner_subject_created_at",
                table: "runs",
                columns: new[] { "owner_subject", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_owner_subject_idempotency_key",
                table: "runs",
                columns: new[] { "owner_subject", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uploads_owner_subject_created_at",
                table: "uploads",
                columns: new[] { "owner_subject", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_instincts");

            migrationBuilder.DropTable(
                name: "model_calls");

            migrationBuilder.DropTable(
                name: "qualifications");

            migrationBuilder.DropTable(
                name: "review_tasks");

            migrationBuilder.DropTable(
                name: "run_events");

            migrationBuilder.DropTable(
                name: "run_phases");

            migrationBuilder.DropTable(
                name: "run_results");

            migrationBuilder.DropTable(
                name: "uploads");

            migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
