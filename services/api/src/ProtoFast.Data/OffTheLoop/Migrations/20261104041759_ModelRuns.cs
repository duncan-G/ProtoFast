using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class ModelRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_runs",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    work = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    progress = table.Column<byte[]>(type: "bytea", nullable: true),
                    result = table.Column<byte[]>(type: "bytea", nullable: true),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    trace_parent = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    note_modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_model_runs_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "loop",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_model_runs_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "loop",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_model_runs_notes_note_id",
                        column: x => x.note_id,
                        principalSchema: "loop",
                        principalTable: "notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_model_runs_application_id",
                schema: "loop",
                table: "model_runs",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_runs_company_id",
                schema: "loop",
                table: "model_runs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_runs_note_id",
                schema: "loop",
                table: "model_runs",
                column: "note_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_runs_status_date_created",
                schema: "loop",
                table: "model_runs",
                columns: new[] { "status", "date_created" });

            migrationBuilder.CreateIndex(
                name: "ix_model_runs_user_id_date_created",
                schema: "loop",
                table: "model_runs",
                columns: new[] { "user_id", "date_created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_runs",
                schema: "loop");
        }
    }
}
