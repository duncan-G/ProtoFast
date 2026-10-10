using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class DirectorJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "director_jobs",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lane = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    steps = table.Column<byte[]>(type: "bytea", nullable: false),
                    step_count = table.Column<int>(type: "integer", nullable: false),
                    steps_done = table.Column<int>(type: "integer", nullable: false),
                    follow_variations = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    results = table.Column<byte[]>(type: "bytea", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_director_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_director_jobs_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_director_jobs_status_date_created",
                schema: "plot",
                table: "director_jobs",
                columns: new[] { "status", "date_created" });

            migrationBuilder.CreateIndex(
                name: "ix_director_jobs_story_id",
                schema: "plot",
                table: "director_jobs",
                column: "story_id");

            migrationBuilder.CreateIndex(
                name: "ix_director_jobs_user_id_date_created",
                schema: "plot",
                table: "director_jobs",
                columns: new[] { "user_id", "date_created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "director_jobs",
                schema: "plot");
        }
    }
}
