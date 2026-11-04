using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cv_id",
                schema: "loop",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resume_id",
                schema: "loop",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "loop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    media_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uploaded = table.Column<bool>(type: "boolean", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_cv_id",
                schema: "loop",
                table: "applications",
                column: "cv_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_resume_id",
                schema: "loop",
                table: "applications",
                column: "resume_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_user_id_date_created",
                schema: "loop",
                table: "documents",
                columns: new[] { "user_id", "date_created" });

            migrationBuilder.AddForeignKey(
                name: "fk_applications_documents_cv_id",
                schema: "loop",
                table: "applications",
                column: "cv_id",
                principalSchema: "loop",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_applications_documents_resume_id",
                schema: "loop",
                table: "applications",
                column: "resume_id",
                principalSchema: "loop",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_documents_cv_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "fk_applications_documents_resume_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "loop");

            migrationBuilder.DropIndex(
                name: "ix_applications_cv_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_applications_resume_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cv_id",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "resume_id",
                schema: "loop",
                table: "applications");
        }
    }
}
