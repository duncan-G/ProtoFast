using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMentionTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_scene_element_mentions_span",
                schema: "plot",
                table: "scene_element_mentions");

            migrationBuilder.AddColumn<bool>(
                name: "is_tag",
                schema: "plot",
                table: "scene_element_mentions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_scene_element_mentions_span",
                schema: "plot",
                table: "scene_element_mentions",
                sql: "\"offset\" >= 0 AND length >= CASE WHEN is_tag THEN 1 ELSE 2 END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM plot.scene_element_mentions WHERE is_tag;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_scene_element_mentions_span",
                schema: "plot",
                table: "scene_element_mentions");

            migrationBuilder.DropColumn(
                name: "is_tag",
                schema: "plot",
                table: "scene_element_mentions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_scene_element_mentions_span",
                schema: "plot",
                table: "scene_element_mentions",
                sql: "\"offset\" >= 0 AND length >= 2");
        }
    }
}
