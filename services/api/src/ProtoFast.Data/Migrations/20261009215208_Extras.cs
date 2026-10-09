using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class Extras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extras",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    wardrobe = table.Column<string>(type: "text", nullable: true),
                    image_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extras", x => x.id);
                    table.ForeignKey(
                        name: "fk_extras_media_assets_image_asset_id",
                        column: x => x.image_asset_id,
                        principalSchema: "plot",
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_extras_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "frame_extras",
                schema: "plot",
                columns: table => new
                {
                    frame_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_screen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_frame_extras", x => new { x.frame_id, x.extra_id });
                    table.ForeignKey(
                        name: "fk_frame_extras_extras_extra_id",
                        column: x => x.extra_id,
                        principalSchema: "plot",
                        principalTable: "extras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_frame_extras_frames_frame_id",
                        column: x => x.frame_id,
                        principalSchema: "plot",
                        principalTable: "frames",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_extras",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_screen = table.Column<bool>(type: "boolean", nullable: false),
                    edited = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_extras", x => x.id);
                    table.ForeignKey(
                        name: "fk_scene_extras_extras_extra_id",
                        column: x => x.extra_id,
                        principalSchema: "plot",
                        principalTable: "extras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_extras_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extras_image_asset_id",
                schema: "plot",
                table: "extras",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_extras_story_id_name",
                schema: "plot",
                table: "extras",
                columns: new[] { "story_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_frame_extras_extra_id",
                schema: "plot",
                table: "frame_extras",
                column: "extra_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_extras_extra_id",
                schema: "plot",
                table: "scene_extras",
                column: "extra_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_extras_scene_id_extra_id",
                schema: "plot",
                table: "scene_extras",
                columns: new[] { "scene_id", "extra_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frame_extras",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "scene_extras",
                schema: "plot");

            migrationBuilder.DropTable(
                name: "extras",
                schema: "plot");
        }
    }
}
