using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationAskedAndLearnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asked",
                schema: "loop",
                table: "applications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "learnings",
                schema: "loop",
                table: "applications",
                type: "jsonb",
                nullable: true);

            // Each line of what was learned becomes an entry of its own, dated when it was last saved.
            migrationBuilder.Sql("""
                UPDATE loop.applications AS a
                SET asked = '[]'::jsonb,
                    learnings = COALESCE((
                        SELECT jsonb_agg(
                            jsonb_build_object('Text', lines.entry, 'Round', '', 'On', to_char(a.date_last_modified, 'YYYY-MM-DD'))
                            ORDER BY lines.n)
                        FROM (
                            SELECT btrim(regexp_replace(t.line, '^\s*[-•*]\s+', '')) AS entry, t.n
                            FROM regexp_split_to_table(a.learned, E'\n') WITH ORDINALITY AS t(line, n)
                        ) AS lines
                        WHERE lines.entry <> ''), '[]'::jsonb);
                """);

            migrationBuilder.DropColumn(
                name: "learned",
                schema: "loop",
                table: "applications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "learned",
                schema: "loop",
                table: "applications",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE loop.applications
                SET learned = left(COALESCE((
                        SELECT string_agg(e->>'Text', E'\n')
                        FROM jsonb_array_elements(COALESCE(learnings, '[]'::jsonb)) AS e), ''), 4000),
                    questions = questions || ARRAY(
                        SELECT e->>'Question'
                        FROM jsonb_array_elements(COALESCE(asked, '[]'::jsonb)) AS e);
                """);

            migrationBuilder.DropColumn(
                name: "asked",
                schema: "loop",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "learnings",
                schema: "loop",
                table: "applications");
        }
    }
}
