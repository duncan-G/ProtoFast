using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class LocationVariations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "location_variation_id",
                schema: "plot",
                table: "frames",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "location_variations",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    image_asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_variations", x => x.id);
                    table.CheckConstraint("ck_location_variations_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_location_variations_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "plot",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_variations_media_assets_image_asset_id",
                        column: x => x.image_asset_id,
                        principalSchema: "plot",
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_frames_location_variation_id",
                schema: "plot",
                table: "frames",
                column: "location_variation_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_variations_image_asset_id",
                schema: "plot",
                table: "location_variations",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_variations_location_id_name",
                schema: "plot",
                table: "location_variations",
                columns: new[] { "location_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_frames_location_variations_location_variation_id",
                schema: "plot",
                table: "frames",
                column: "location_variation_id",
                principalSchema: "plot",
                principalTable: "location_variations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_frames_location_variations_location_variation_id",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropTable(
                name: "location_variations",
                schema: "plot");

            migrationBuilder.DropIndex(
                name: "ix_frames_location_variation_id",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropColumn(
                name: "location_variation_id",
                schema: "plot",
                table: "frames");
        }
    }
}
