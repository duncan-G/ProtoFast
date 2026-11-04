using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loop");

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    about = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    closed = table.Column<bool>(type: "boolean", nullable: false),
                    applied_on = table.Column<DateOnly>(type: "date", nullable: false),
                    resume = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    next_step = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    next_on = table.Column<DateOnly>(type: "date", nullable: true),
                    revenue = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    growth = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    headcount = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    funding = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    wants = table.Column<List<string>>(type: "text[]", nullable: false),
                    values = table.Column<List<string>>(type: "text[]", nullable: false),
                    practices = table.Column<List<string>>(type: "text[]", nullable: false),
                    learned = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    contacts = table.Column<string>(type: "jsonb", nullable: true),
                    rounds = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "todos",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    done_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notes",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tag = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tell = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    move = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    code = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    badges = table.Column<List<string>>(type: "text[]", nullable: false),
                    fits = table.Column<List<string>>(type: "text[]", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_notes_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "loop",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_user_id",
                schema: "loop",
                table: "applications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notes_application_id",
                schema: "loop",
                table: "notes",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_notes_user_id_date_last_modified",
                schema: "loop",
                table: "notes",
                columns: new[] { "user_id", "date_last_modified" });

            migrationBuilder.CreateIndex(
                name: "ix_todos_user_id",
                schema: "loop",
                table: "todos",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notes",
                schema: "loop");

            migrationBuilder.DropTable(
                name: "todos",
                schema: "loop");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "loop");
        }
    }
}
