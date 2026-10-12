using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class LocationWorldModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "inside_location_id",
                schema: "plot",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "location_objects",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_location_objects_props_prop_id",
                        column: x => x.prop_id,
                        principalSchema: "plot",
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_location_objects_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_parts",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_part_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_parts", x => x.id);
                    table.CheckConstraint("ck_location_parts_position_non_negative", "position IS NULL OR position >= 0");
                    table.ForeignKey(
                        name: "fk_location_parts_location_parts_parent_part_id",
                        column: x => x.parent_part_id,
                        principalSchema: "plot",
                        principalTable: "location_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_parts_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "world_model_builds",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scenes_total = table.Column<int>(type: "integer", nullable: false),
                    scenes_done = table.Column<int>(type: "integer", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    failure = table.Column<string>(type: "text", nullable: true),
                    deliveries = table.Column<int>(type: "integer", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    part_count = table.Column<int>(type: "integer", nullable: false),
                    object_count = table.Column<int>(type: "integer", nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_world_model_builds", x => x.id);
                    table.CheckConstraint("ck_world_model_builds_counts_non_negative", "scenes_total >= 0 AND scenes_done >= 0 AND deliveries >= 0 AND part_count >= 0 AND object_count >= 0 AND observation_count >= 0");
                    table.ForeignKey(
                        name: "fk_world_model_builds_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_observations",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edge = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    place_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    place_part_id = table.Column<Guid>(type: "uuid", nullable: true),
                    other_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    other_part_id = table.Column<Guid>(type: "uuid", nullable: true),
                    via_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    property = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    basis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_observations", x => x.id);
                    table.CheckConstraint("ck_location_observations_at_columns", "edge <> 'At' OR (object_id IS NOT NULL AND character_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
                    table.CheckConstraint("ck_location_observations_beat_at_columns", "edge <> 'BeatAt' OR (place_location_id IS NOT NULL AND object_id IS NULL AND character_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
                    table.CheckConstraint("ck_location_observations_character_at_columns", "edge <> 'CharacterAt' OR (character_id IS NOT NULL AND place_location_id IS NOT NULL AND object_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
                    table.CheckConstraint("ck_location_observations_connects_columns", "edge <> 'Connects' OR (place_location_id IS NOT NULL AND other_location_id IS NOT NULL AND object_id IS NULL AND character_id IS NULL AND property IS NULL)");
                    table.CheckConstraint("ck_location_observations_parts_have_locations", "(place_part_id IS NULL OR place_location_id IS NOT NULL) AND (other_part_id IS NULL OR other_location_id IS NOT NULL)");
                    table.CheckConstraint("ck_location_observations_state_columns", "edge <> 'State' OR (object_id IS NOT NULL AND property IS NOT NULL AND value IS NOT NULL AND place_location_id IS NULL AND place_part_id IS NULL AND character_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_location_observations_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_location_objects_object_id",
                        column: x => x.object_id,
                        principalSchema: "plot",
                        principalTable: "location_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_location_objects_via_object_id",
                        column: x => x.via_object_id,
                        principalSchema: "plot",
                        principalTable: "location_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_location_observations_location_parts_other_part_id",
                        column: x => x.other_part_id,
                        principalSchema: "plot",
                        principalTable: "location_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_location_parts_place_part_id",
                        column: x => x.place_part_id,
                        principalSchema: "plot",
                        principalTable: "location_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_locations_other_location_id",
                        column: x => x.other_location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_locations_place_location_id",
                        column: x => x.place_location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_observations_scene_elements_scene_element_id",
                        column: x => x.scene_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "world_model_build_steps",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    model_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    reply = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    failure = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_world_model_build_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_world_model_build_steps_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_world_model_build_steps_world_model_builds_build_id",
                        column: x => x.build_id,
                        principalSchema: "plot",
                        principalTable: "world_model_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_locations_inside_location_id",
                schema: "plot",
                table: "locations",
                column: "inside_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_objects_prop_id",
                schema: "plot",
                table: "location_objects",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_objects_story_id_name",
                schema: "plot",
                table: "location_objects",
                columns: new[] { "story_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_character_id",
                schema: "plot",
                table: "location_observations",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_object_id",
                schema: "plot",
                table: "location_observations",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_other_location_id",
                schema: "plot",
                table: "location_observations",
                column: "other_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_other_part_id",
                schema: "plot",
                table: "location_observations",
                column: "other_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_place_location_id",
                schema: "plot",
                table: "location_observations",
                column: "place_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_place_part_id",
                schema: "plot",
                table: "location_observations",
                column: "place_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_scene_element_id",
                schema: "plot",
                table: "location_observations",
                column: "scene_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_observations_via_object_id",
                schema: "plot",
                table: "location_observations",
                column: "via_object_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_parts_location_id",
                schema: "plot",
                table: "location_parts",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_parts_parent_part_id",
                schema: "plot",
                table: "location_parts",
                column: "parent_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_world_model_build_steps_build_id_sequence",
                schema: "plot",
                table: "world_model_build_steps",
                columns: new[] { "build_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_world_model_build_steps_scene_id",
                schema: "plot",
                table: "world_model_build_steps",
                column: "scene_id");

            migrationBuilder.CreateIndex(
                name: "ix_world_model_builds_date_created",
                schema: "plot",
                table: "world_model_builds",
                column: "date_created");

            migrationBuilder.CreateIndex(
                name: "ix_world_model_builds_story_id_date_created",
                schema: "plot",
                table: "world_model_builds",
                columns: new[] { "story_id", "date_created" });

            migrationBuilder.AddForeignKey(
                name: "fk_locations_locations_inside_location_id",
                schema: "plot",
                table: "locations",
                column: "inside_location_id",
                principalSchema: "plot",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_locations_locations_inside_location_id",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropTable(
                name: "location_observations",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "world_model_build_steps",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "location_objects",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "location_parts",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "world_model_builds",
                schema: "plot");

            migrationBuilder.DropIndex(
                name: "ix_locations_inside_location_id",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "inside_location_id",
                schema: "plot",
                table: "locations");
        }
    }
}
