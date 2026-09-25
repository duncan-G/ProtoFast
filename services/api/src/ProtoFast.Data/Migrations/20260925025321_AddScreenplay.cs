using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stories",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_document_id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stories", x => x.id);
                    table.ForeignKey(
                        name: "fk_stories_documents_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "plot",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "character_kinds",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_kinds", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_kinds_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "containers",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containers", x => x.id);
                    table.CheckConstraint("ck_containers_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_containers_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    setting = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hue = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                    table.CheckConstraint("ck_locations_hue", "hue BETWEEN 0 AND 359");
                    table.ForeignKey(
                        name: "fk_locations_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "props",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_props", x => x.id);
                    table.ForeignKey(
                        name: "fk_props_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "times_of_day",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_times_of_day", x => x.id);
                    table.ForeignKey(
                        name: "fk_times_of_day_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transitions",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transitions_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hue = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_characters", x => x.id);
                    table.CheckConstraint("ck_characters_hue", "hue BETWEEN 0 AND 359");
                    table.ForeignKey(
                        name: "fk_characters_character_kinds_kind_id",
                        column: x => x.kind_id,
                        principalSchema: "plot",
                        principalTable: "character_kinds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_characters_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenes",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scenes", x => x.id);
                    table.CheckConstraint("ck_scenes_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_scenes_containers_container_id",
                        column: x => x.container_id,
                        principalSchema: "plot",
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_elements",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    time_of_day_id = table.Column<Guid>(type: "uuid", nullable: true),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parenthetical = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    transition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_elements", x => x.id);
                    table.CheckConstraint("ck_scene_elements_dialogue_columns", "type = 'Dialogue' OR (speaker_id IS NULL AND parenthetical IS NULL)");
                    table.CheckConstraint("ck_scene_elements_heading_columns", "type = 'Heading' OR (location_id IS NULL AND time_of_day_id IS NULL)");
                    table.CheckConstraint("ck_scene_elements_position_non_negative", "position >= 0");
                    table.CheckConstraint("ck_scene_elements_text_columns", "(type IN ('Heading', 'Transition')) = (text IS NULL)");
                    table.CheckConstraint("ck_scene_elements_transition_columns", "type = 'Transition' OR transition_id IS NULL");
                    table.ForeignKey(
                        name: "fk_scene_elements_characters_speaker_id",
                        column: x => x.speaker_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_elements_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_elements_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_elements_times_of_day_time_of_day_id",
                        column: x => x.time_of_day_id,
                        principalSchema: "plot",
                        principalTable: "times_of_day",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_elements_transitions_transition_id",
                        column: x => x.transition_id,
                        principalSchema: "plot",
                        principalTable: "transitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scene_element_mentions",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offset = table.Column<int>(type: "integer", nullable: false),
                    length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_element_mentions", x => x.id);
                    table.CheckConstraint("ck_scene_element_mentions_one_target", "num_nonnulls(character_id, prop_id, location_id) = 1");
                    table.CheckConstraint("ck_scene_element_mentions_span", "\"offset\" >= 0 AND length >= 2");
                    table.ForeignKey(
                        name: "fk_scene_element_mentions_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_element_mentions_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_element_mentions_props_prop_id",
                        column: x => x.prop_id,
                        principalSchema: "plot",
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_element_mentions_scene_elements_scene_element_id",
                        column: x => x.scene_element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_character_kinds_story_id_label",
                schema: "plot",
                table: "character_kinds",
                columns: new[] { "story_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_characters_kind_id",
                schema: "plot",
                table: "characters",
                column: "kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_characters_story_id_name",
                schema: "plot",
                table: "characters",
                columns: new[] { "story_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_containers_story_id_position",
                schema: "plot",
                table: "containers",
                columns: new[] { "story_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_story_id_name",
                schema: "plot",
                table: "locations",
                columns: new[] { "story_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_props_story_id_name",
                schema: "plot",
                table: "props",
                columns: new[] { "story_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scene_element_mentions_character_id",
                schema: "plot",
                table: "scene_element_mentions",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_element_mentions_location_id",
                schema: "plot",
                table: "scene_element_mentions",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_element_mentions_prop_id",
                schema: "plot",
                table: "scene_element_mentions",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_element_mentions_scene_element_id",
                schema: "plot",
                table: "scene_element_mentions",
                column: "scene_element_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_elements_location_id",
                schema: "plot",
                table: "scene_elements",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_elements_scene_id_position",
                schema: "plot",
                table: "scene_elements",
                columns: new[] { "scene_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_scene_elements_speaker_id",
                schema: "plot",
                table: "scene_elements",
                column: "speaker_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_elements_time_of_day_id",
                schema: "plot",
                table: "scene_elements",
                column: "time_of_day_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_elements_transition_id",
                schema: "plot",
                table: "scene_elements",
                column: "transition_id");

            migrationBuilder.CreateIndex(
                name: "ix_scenes_container_id_position",
                schema: "plot",
                table: "scenes",
                columns: new[] { "container_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_stories_source_document_id",
                schema: "plot",
                table: "stories",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stories_user_id_date_last_modified",
                schema: "plot",
                table: "stories",
                columns: new[] { "user_id", "date_last_modified" });

            migrationBuilder.CreateIndex(
                name: "ix_times_of_day_story_id_label",
                schema: "plot",
                table: "times_of_day",
                columns: new[] { "story_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transitions_story_id_label",
                schema: "plot",
                table: "transitions",
                columns: new[] { "story_id", "label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scene_element_mentions",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "props",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scene_elements",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "characters",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scenes",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "times_of_day",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "transitions",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "character_kinds",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "containers",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "stories",
                schema: "plot");
        }
    }
}
