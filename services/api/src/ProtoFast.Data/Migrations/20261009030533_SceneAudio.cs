using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class SceneAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "narrator_voice",
                schema: "plot",
                table: "stories",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "audio_asset_id",
                schema: "plot",
                table: "cues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "plot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    story_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    voice = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    cost_usd_micros = table.Column<long>(type: "bigint", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_assets_stories_story_id",
                        column: x => x.story_id,
                        principalSchema: "plot",
                        principalTable: "stories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cues_audio_asset_id",
                schema: "plot",
                table: "cues",
                column: "audio_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_story_id_kind_request_hash",
                schema: "plot",
                table: "media_assets",
                columns: new[] { "story_id", "kind", "request_hash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cues_media_assets_audio_asset_id",
                schema: "plot",
                table: "cues",
                column: "audio_asset_id",
                principalSchema: "plot",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cues_media_assets_audio_asset_id",
                schema: "plot",
                table: "cues");

            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "plot");

            migrationBuilder.DropIndex(
                name: "ix_cues_audio_asset_id",
                schema: "plot",
                table: "cues");

            migrationBuilder.DropColumn(
                name: "narrator_voice",
                schema: "plot",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "audio_asset_id",
                schema: "plot",
                table: "cues");
        }
    }
}
