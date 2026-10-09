using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class DirectorLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "story_day",
                schema: "plot",
                table: "scenes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "plot",
                table: "props",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acoustics",
                schema: "plot",
                table: "locations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appearance",
                schema: "plot",
                table: "locations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "plot",
                table: "locations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appearance",
                schema: "plot",
                table: "characters",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "plot",
                table: "characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice",
                schema: "plot",
                table: "characters",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "character_wardrobes",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_wardrobes", x => x.id);
                    table.CheckConstraint("ck_character_wardrobes_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_character_wardrobes_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cues",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performance = table.Column<string>(type: "text", nullable: false),
                    gap_before_ms = table.Column<int>(type: "integer", nullable: false),
                    interrupted = table.Column<bool>(type: "boolean", nullable: false),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cues", x => x.id);
                    table.ForeignKey(
                        name: "fk_cues_scene_elements_element_id",
                        column: x => x.element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cues_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panels",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    first_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    time_of_day = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    beat = table.Column<string>(type: "text", nullable: false),
                    shot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hold_ms = table.Column<int>(type: "integer", nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panels", x => x.id);
                    table.CheckConstraint("ck_panels_hold_ms", "hold_ms IS NULL OR hold_ms > 0");
                    table.CheckConstraint("ck_panels_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_panels_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_panels_scene_elements_first_element_id",
                        column: x => x.first_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_panels_scene_elements_last_element_id",
                        column: x => x.last_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_panels_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_prop_states",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<List<string>>(type: "text[]", nullable: false),
                    change_reason = table.Column<string>(type: "text", nullable: true),
                    change_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_prop_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_scene_prop_states_props_prop_id",
                        column: x => x.prop_id,
                        principalSchema: "plot",
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_prop_states_scene_elements_change_element_id",
                        column: x => x.change_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_prop_states_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_appearances",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wardrobe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<List<string>>(type: "text[]", nullable: false),
                    change_reason = table.Column<string>(type: "text", nullable: true),
                    change_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_appearances", x => x.id);
                    table.ForeignKey(
                        name: "fk_scene_appearances_character_wardrobes_wardrobe_id",
                        column: x => x.wardrobe_id,
                        principalSchema: "plot",
                        principalTable: "character_wardrobes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_appearances_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_appearances_scene_elements_change_element_id",
                        column: x => x.change_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_appearances_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panel_placements",
                schema: "plot",
                columns: table => new
                {
                    panel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    screen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pose = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expression = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    state = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panel_placements", x => new { x.panel_id, x.character_id });
                    table.ForeignKey(
                        name: "fk_panel_placements_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_panel_placements_panels_panel_id",
                        column: x => x.panel_id,
                        principalSchema: "plot",
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panel_props",
                schema: "plot",
                columns: table => new
                {
                    panel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panel_props", x => new { x.panel_id, x.prop_id });
                    table.ForeignKey(
                        name: "fk_panel_props_panels_panel_id",
                        column: x => x.panel_id,
                        principalSchema: "plot",
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_panel_props_props_prop_id",
                        column: x => x.prop_id,
                        principalSchema: "plot",
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_scenes_story_day",
                schema: "plot",
                table: "scenes",
                sql: "story_day IS NULL OR story_day >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_character_wardrobes_character_id_label",
                schema: "plot",
                table: "character_wardrobes",
                columns: new[] { "character_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cues_element_id",
                schema: "plot",
                table: "cues",
                column: "element_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cues_scene_id",
                schema: "plot",
                table: "cues",
                column: "scene_id");

            migrationBuilder.CreateIndex(
                name: "ix_panel_placements_character_id",
                schema: "plot",
                table: "panel_placements",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_panel_props_prop_id",
                schema: "plot",
                table: "panel_props",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_first_element_id",
                schema: "plot",
                table: "panels",
                column: "first_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_last_element_id",
                schema: "plot",
                table: "panels",
                column: "last_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_location_id",
                schema: "plot",
                table: "panels",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_scene_id_position",
                schema: "plot",
                table: "panels",
                columns: new[] { "scene_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_scene_appearances_change_element_id",
                schema: "plot",
                table: "scene_appearances",
                column: "change_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_appearances_character_id",
                schema: "plot",
                table: "scene_appearances",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_appearances_scene_id_character_id",
                schema: "plot",
                table: "scene_appearances",
                columns: new[] { "scene_id", "character_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scene_appearances_wardrobe_id",
                schema: "plot",
                table: "scene_appearances",
                column: "wardrobe_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_prop_states_change_element_id",
                schema: "plot",
                table: "scene_prop_states",
                column: "change_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_prop_states_prop_id",
                schema: "plot",
                table: "scene_prop_states",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_prop_states_scene_id_prop_id",
                schema: "plot",
                table: "scene_prop_states",
                columns: new[] { "scene_id", "prop_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                UPDATE plot.stories
                SET vocabulary = jsonb_set(vocabulary, '{DeliveryTags}', '[]')
                WHERE NOT vocabulary ? 'DeliveryTags';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE plot.stories SET vocabulary = vocabulary - 'DeliveryTags';");

            migrationBuilder.DropTable(
                name: "cues",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "panel_placements",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "panel_props",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scene_appearances",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scene_prop_states",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "panels",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "character_wardrobes",
                schema: "plot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_scenes_story_day",
                schema: "plot",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "story_day",
                schema: "plot",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "plot",
                table: "props");

            migrationBuilder.DropColumn(
                name: "acoustics",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "appearance",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "appearance",
                schema: "plot",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "plot",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "voice",
                schema: "plot",
                table: "characters");
        }
    }
}
