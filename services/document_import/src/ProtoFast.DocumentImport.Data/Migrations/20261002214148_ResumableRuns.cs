using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResumableRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "abandoned_at",
                schema: "engine",
                table: "runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure",
                schema: "engine",
                table: "runs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "run_messages",
                schema: "engine",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "jsonb", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_messages", x => new { x.run_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_run_messages_runs_run_id",
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
                name: "run_messages",
                schema: "engine");

            migrationBuilder.DropColumn(
                name: "abandoned_at",
                schema: "engine",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "failure",
                schema: "engine",
                table: "runs");
        }
    }
}
