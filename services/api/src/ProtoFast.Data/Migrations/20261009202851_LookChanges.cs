using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class LookChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "holding",
                schema: "plot",
                table: "scene_appearances",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "image_asset_id",
                schema: "plot",
                table: "scene_appearances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scene_look_changes",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wardrobe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wardrobe_change = table.Column<string>(type: "text", nullable: false),
                    appearance_change = table.Column<string>(type: "text", nullable: false),
                    holding = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false),
                    image_asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scene_look_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_scene_look_changes_character_wardrobes_wardrobe_id",
                        column: x => x.wardrobe_id,
                        principalSchema: "plot",
                        principalTable: "character_wardrobes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_look_changes_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "plot",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_look_changes_media_assets_image_asset_id",
                        column: x => x.image_asset_id,
                        principalSchema: "plot",
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scene_look_changes_scene_elements_element_id",
                        column: x => x.element_id,
                        principalSchema: "plot",
                        principalTable: "scene_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scene_look_changes_scenes_scene_id",
                        column: x => x.scene_id,
                        principalSchema: "plot",
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scene_appearances_image_asset_id",
                schema: "plot",
                table: "scene_appearances",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_look_changes_character_id",
                schema: "plot",
                table: "scene_look_changes",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_look_changes_element_id",
                schema: "plot",
                table: "scene_look_changes",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_look_changes_image_asset_id",
                schema: "plot",
                table: "scene_look_changes",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_scene_look_changes_scene_id_character_id_element_id",
                schema: "plot",
                table: "scene_look_changes",
                columns: new[] { "scene_id", "character_id", "element_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scene_look_changes_wardrobe_id",
                schema: "plot",
                table: "scene_look_changes",
                column: "wardrobe_id");

            migrationBuilder.AddForeignKey(
                name: "fk_scene_appearances_media_assets_image_asset_id",
                schema: "plot",
                table: "scene_appearances",
                column: "image_asset_id",
                principalSchema: "plot",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_scene_appearances_media_assets_image_asset_id",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropTable(
                name: "scene_look_changes",
                schema: "plot");

            migrationBuilder.DropIndex(
                name: "ix_scene_appearances_image_asset_id",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "holding",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "image_asset_id",
                schema: "plot",
                table: "scene_appearances");
        }
    }
}
