using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class DialogueExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_scene_elements_dialogue_columns",
                schema: "plot",
                table: "scene_elements");

            migrationBuilder.AddColumn<string>(
                name: "extension",
                schema: "plot",
                table: "scene_elements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_scene_elements_dialogue_columns",
                schema: "plot",
                table: "scene_elements",
                sql: "type = 'Dialogue' OR (speaker_id IS NULL AND extension IS NULL AND parenthetical IS NULL)");

            migrationBuilder.Sql(
                """
                UPDATE plot.stories
                SET vocabulary = jsonb_set(vocabulary, '{Extensions}', '[]')
                WHERE NOT vocabulary ? 'Extensions';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE plot.stories SET vocabulary = vocabulary - 'Extensions';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_scene_elements_dialogue_columns",
                schema: "plot",
                table: "scene_elements");

            migrationBuilder.DropColumn(
                name: "extension",
                schema: "plot",
                table: "scene_elements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_scene_elements_dialogue_columns",
                schema: "plot",
                table: "scene_elements",
                sql: "type = 'Dialogue' OR (speaker_id IS NULL AND parenthetical IS NULL)");
        }
    }
}
