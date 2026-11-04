using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class NoteUnpinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_notes_applications_application_id",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "ix_notes_application_id",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "application_id",
                schema: "loop",
                table: "notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "application_id",
                schema: "loop",
                table: "notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notes_application_id",
                schema: "loop",
                table: "notes",
                column: "application_id");

            migrationBuilder.AddForeignKey(
                name: "fk_notes_applications_application_id",
                schema: "loop",
                table: "notes",
                column: "application_id",
                principalSchema: "loop",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
