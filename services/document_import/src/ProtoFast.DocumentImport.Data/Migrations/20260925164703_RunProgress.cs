using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.DocumentImport.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "run_progress",
                schema: "engine",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    stage_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    result_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_progress", x => x.source_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_run_progress_run_id",
                schema: "engine",
                table: "run_progress",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_progress",
                schema: "engine");
        }
    }
}
