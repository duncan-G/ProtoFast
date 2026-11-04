using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class Resumes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resumes",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    headline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    notes = table.Column<List<string>>(type: "text[]", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    education = table.Column<string>(type: "jsonb", nullable: true),
                    experience = table.Column<string>(type: "jsonb", nullable: true),
                    extras = table.Column<string>(type: "jsonb", nullable: true),
                    links = table.Column<string>(type: "jsonb", nullable: true),
                    projects = table.Column<string>(type: "jsonb", nullable: true),
                    skills = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resumes", x => x.id);
                    table.ForeignKey(
                        name: "fk_resumes_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "loop",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resumes_application_id",
                schema: "loop",
                table: "resumes",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_resumes_user_id_application_id",
                schema: "loop",
                table: "resumes",
                columns: new[] { "user_id", "application_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resumes",
                schema: "loop");
        }
    }
}
