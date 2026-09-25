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
                name: "cast_members",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hue = table.Column<int>(type: "integer", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cast_members", x => x.id);
                    table.CheckConstraint("ck_cast_members_hue", "hue BETWEEN 0 AND 359");
                    table.ForeignKey(
                        name: "fk_cast_members_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drafts",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drafts", x => x.id);
                    table.CheckConstraint("ck_drafts_number_positive", "number >= 1");
                    table.ForeignKey(
                        name: "fk_drafts_stories_story_id",
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
                name: "acts",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acts", x => x.id);
                    table.CheckConstraint("ck_acts_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_acts_drafts_draft_id",
                        column: x => x.draft_id,
                        principalSchema: "plot",
                        principalTable: "drafts",
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
                    act_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "fk_scenes_acts_act_id",
                        column: x => x.act_id,
                        principalSchema: "plot",
                        principalTable: "acts",
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
                    time_of_day = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parenthetical = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    transition_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_elements", x => x.id);
                    table.CheckConstraint("ck_scene_elements_dialogue_columns", "type = 'Dialogue' OR (speaker_id IS NULL AND parenthetical IS NULL)");
                    table.CheckConstraint("ck_scene_elements_heading_columns", "(type = 'Heading') = (time_of_day IS NOT NULL) AND (location_id IS NULL OR type = 'Heading')");
                    table.CheckConstraint("ck_scene_elements_position_non_negative", "position >= 0");
                    table.CheckConstraint("ck_scene_elements_text_columns", "(type IN ('Heading', 'Transition')) = (text IS NULL)");
                    table.CheckConstraint("ck_scene_elements_transition_columns", "(type = 'Transition') = (transition_kind IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_scene_elements_cast_members_speaker_id",
                        column: x => x.speaker_id,
                        principalSchema: "plot",
                        principalTable: "cast_members",
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
                });

            migrationBuilder.CreateTable(
                name: "scene_element_mentions",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    scene_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cast_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offset = table.Column<int>(type: "integer", nullable: false),
                    length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_element_mentions", x => x.id);
                    table.CheckConstraint("ck_scene_element_mentions_one_target", "(cast_member_id IS NULL) <> (prop_id IS NULL)");
                    table.CheckConstraint("ck_scene_element_mentions_span", "\"offset\" >= 0 AND length >= 2");
                    table.ForeignKey(
                        name: "fk_scene_element_mentions_cast_members_cast_member_id",
                        column: x => x.cast_member_id,
                        principalSchema: "plot",
                        principalTable: "cast_members",
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
                name: "ix_acts_draft_id_position",
                schema: "plot",
                table: "acts",
                columns: new[] { "draft_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_cast_members_story_id_name",
                schema: "plot",
                table: "cast_members",
                columns: new[] { "story_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drafts_story_id_number",
                schema: "plot",
                table: "drafts",
                columns: new[] { "story_id", "number" },
                unique: true);

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
                name: "ix_scene_element_mentions_cast_member_id",
                schema: "plot",
                table: "scene_element_mentions",
                column: "cast_member_id");

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
                name: "ix_scenes_act_id_position",
                schema: "plot",
                table: "scenes",
                columns: new[] { "act_id", "position" });

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
                name: "cast_members",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scenes",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "acts",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "drafts",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "stories",
                schema: "plot");
        }
    }
}
