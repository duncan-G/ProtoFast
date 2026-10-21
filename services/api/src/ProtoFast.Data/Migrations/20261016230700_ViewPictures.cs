using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class ViewPictures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "view_pictures",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    camera_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    camera_part = table.Column<string>(type: "text", nullable: true),
                    toward_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    toward_part = table.Column<string>(type: "text", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    cost_usd_micros = table.Column<long>(type: "bigint", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_view_pictures", x => x.id);
                    table.CheckConstraint("ck_view_pictures_toward_part_has_location", "toward_part IS NULL OR toward_location_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_view_pictures_locations_camera_location_id",
                        column: x => x.camera_location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_view_pictures_locations_toward_location_id",
                        column: x => x.toward_location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_view_pictures_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_view_pictures_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_camera_location_id",
                schema: "plot",
                table: "view_pictures",
                column: "camera_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_view",
                schema: "plot",
                table: "view_pictures",
                columns: new[] { "scene_id", "camera_location_id", "camera_part", "toward_location_id", "toward_part" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_story_id",
                schema: "plot",
                table: "view_pictures",
                column: "story_id");

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_toward_location_id",
                schema: "plot",
                table: "view_pictures",
                column: "toward_location_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "view_pictures",
                schema: "plot");
        }
    }
}
