using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunSystemPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "run_system_prompts",
                schema: "engine",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_sequence = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_system_prompts", x => new { x.run_id, x.from_sequence });
                    table.ForeignKey(
                        name: "fk_run_system_prompts_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "engine",
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_system_prompts",
                schema: "engine");
        }
    }
}
