using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class NoteBody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "body",
                schema: "loop",
                table: "notes",
                type: "character varying(50000)",
                maxLength: 50000,
                nullable: false,
                defaultValue: "");

            // The tell, the move, then the code as a fence: what the note page showed, in order.
            migrationBuilder.Sql("""
                UPDATE loop.notes SET body = concat_ws(E'\n\n',
                    NULLIF(tell, ''),
                    NULLIF(move, ''),
                    CASE WHEN code <> '' THEN E'```\n' || code || E'\n```' END);
                """);

            migrationBuilder.DropColumn(
                name: "code",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "move",
                schema: "loop",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "tell",
                schema: "loop",
                table: "notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "loop",
                table: "notes",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "move",
                schema: "loop",
                table: "notes",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tell",
                schema: "loop",
                table: "notes",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            // A page doesn't split back into parts; the start of it becomes the tell.
            migrationBuilder.Sql("UPDATE loop.notes SET tell = left(body, 4000);");

            migrationBuilder.DropColumn(
                name: "body",
                schema: "loop",
                table: "notes");
        }
    }
}
