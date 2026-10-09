using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class DirectorRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "angle",
                schema: "plot",
                table: "scenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "movement",
                schema: "plot",
                table: "scenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shot",
                schema: "plot",
                table: "scenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "in_view",
                schema: "plot",
                table: "scene_prop_states",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "appearance_change",
                schema: "plot",
                table: "scene_appearances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "on_screen",
                schema: "plot",
                table: "scene_appearances",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "wardrobe_change",
                schema: "plot",
                table: "scene_appearances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "shot",
                schema: "plot",
                table: "panels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "angle",
                schema: "plot",
                table: "panels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_change",
                schema: "plot",
                table: "panels",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "movement",
                schema: "plot",
                table: "panels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "in_view",
                schema: "plot",
                table: "panel_props",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "appearance_change",
                schema: "plot",
                table: "panel_placements",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "on_screen",
                schema: "plot",
                table: "panel_placements",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wardrobe_change",
                schema: "plot",
                table: "panel_placements",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "wardrobe_id",
                schema: "plot",
                table: "panel_placements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "narrator_id",
                schema: "plot",
                table: "cues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "narrators",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    voice = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_narrators", x => x.id);
                    table.CheckConstraint("ck_narrators_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_narrators_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_location_states",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change = table.Column<string>(type: "text", nullable: false),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_location_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_scene_location_states_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_location_states_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_panel_placements_wardrobe_id",
                schema: "plot",
                table: "panel_placements",
                column: "wardrobe_id");

            migrationBuilder.CreateIndex(
                name: "ix_cues_narrator_id",
                schema: "plot",
                table: "cues",
                column: "narrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_narrators_story_id_position",
                schema: "plot",
                table: "narrators",
                columns: new[] { "story_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_scene_location_states_location_id",
                schema: "plot",
                table: "scene_location_states",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_location_states_scene_id_location_id",
                schema: "plot",
                table: "scene_location_states",
                columns: new[] { "scene_id", "location_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cues_narrators_narrator_id",
                schema: "plot",
                table: "cues",
                column: "narrator_id",
                principalSchema: "plot",
                principalTable: "narrators",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_panel_placements_character_wardrobes_wardrobe_id",
                schema: "plot",
                table: "panel_placements",
                column: "wardrobe_id",
                principalSchema: "plot",
                principalTable: "character_wardrobes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
            // What the dropped columns held moves to the text that replaces it.
            migrationBuilder.Sql("""
                UPDATE plot.scene_appearances SET appearance_change = array_to_string(state, ', ');
                UPDATE plot.panel_placements SET appearance_change = array_to_string(state, ', '), on_screen = TRUE;
                INSERT INTO plot.narrators (id, story_id, position, name, voice)
                SELECT gen_random_uuid(), id, 0, 'Narrator', narrator_voice
                FROM plot.stories WHERE narrator_voice IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "narrator_voice",
                schema: "plot",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "plot",
                table: "panel_placements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cues_narrators_narrator_id",
                schema: "plot",
                table: "cues");

            migrationBuilder.DropForeignKey(
                name: "fk_panel_placements_character_wardrobes_wardrobe_id",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropTable(
                name: "narrators",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scene_location_states",
                schema: "plot");

            migrationBuilder.DropIndex(
                name: "ix_panel_placements_wardrobe_id",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropIndex(
                name: "ix_cues_narrator_id",
                schema: "plot",
                table: "cues");

            migrationBuilder.DropColumn(
                name: "angle",
                schema: "plot",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "movement",
                schema: "plot",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "shot",
                schema: "plot",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "in_view",
                schema: "plot",
                table: "scene_prop_states");

            migrationBuilder.DropColumn(
                name: "appearance_change",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "on_screen",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "wardrobe_change",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "angle",
                schema: "plot",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "location_change",
                schema: "plot",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "movement",
                schema: "plot",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "in_view",
                schema: "plot",
                table: "panel_props");

            migrationBuilder.DropColumn(
                name: "appearance_change",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropColumn(
                name: "on_screen",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropColumn(
                name: "wardrobe_change",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropColumn(
                name: "wardrobe_id",
                schema: "plot",
                table: "panel_placements");

            migrationBuilder.DropColumn(
                name: "narrator_id",
                schema: "plot",
                table: "cues");

            migrationBuilder.AddColumn<string>(
                name: "narrator_voice",
                schema: "plot",
                table: "stories",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "state",
                schema: "plot",
                table: "scene_appearances",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.Sql("UPDATE plot.panels SET shot = 'medium' WHERE shot IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "shot",
                schema: "plot",
                table: "panels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "state",
                schema: "plot",
                table: "panel_placements",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }
    }
}
