using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneratedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "image_asset_id",
                schema: "plot",
                table: "props",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "plot",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt",
                schema: "plot",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "references",
                schema: "plot",
                table: "media_assets",
                type: "jsonb",
                nullable: true);

            // Spoken lines were composed from nothing.
            migrationBuilder.Sql("UPDATE plot.media_assets SET \"references\" = '[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "image_asset_id",
                schema: "plot",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_asset_id",
                schema: "plot",
                table: "frames",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_asset_id",
                schema: "plot",
                table: "characters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_props_image_asset_id",
                schema: "plot",
                table: "props",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_image_asset_id",
                schema: "plot",
                table: "locations",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_frames_image_asset_id",
                schema: "plot",
                table: "frames",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_characters_image_asset_id",
                schema: "plot",
                table: "characters",
                column: "image_asset_id");

            migrationBuilder.AddForeignKey(
                name: "fk_characters_media_assets_image_asset_id",
                schema: "plot",
                table: "characters",
                column: "image_asset_id",
                principalSchema: "plot",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_frames_media_assets_image_asset_id",
                schema: "plot",
                table: "frames",
                column: "image_asset_id",
                principalSchema: "plot",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_locations_media_assets_image_asset_id",
                schema: "plot",
                table: "locations",
                column: "image_asset_id",
                principalSchema: "plot",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_props_media_assets_image_asset_id",
                schema: "plot",
                table: "props",
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
                name: "fk_characters_media_assets_image_asset_id",
                schema: "plot",
                table: "characters");

            migrationBuilder.DropForeignKey(
                name: "fk_frames_media_assets_image_asset_id",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropForeignKey(
                name: "fk_locations_media_assets_image_asset_id",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "fk_props_media_assets_image_asset_id",
                schema: "plot",
                table: "props");

            migrationBuilder.DropIndex(
                name: "ix_props_image_asset_id",
                schema: "plot",
                table: "props");

            migrationBuilder.DropIndex(
                name: "ix_locations_image_asset_id",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_frames_image_asset_id",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropIndex(
                name: "ix_characters_image_asset_id",
                schema: "plot",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "image_asset_id",
                schema: "plot",
                table: "props");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "plot",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "prompt",
                schema: "plot",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "references",
                schema: "plot",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "image_asset_id",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "image_asset_id",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropColumn(
                name: "image_asset_id",
                schema: "plot",
                table: "characters");
        }
    }
}
