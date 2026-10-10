using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class FramePositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "depth",
                schema: "plot",
                table: "frame_props",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "mark",
                schema: "plot",
                table: "frame_props",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "screen",
                schema: "plot",
                table: "frame_props",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "depth",
                schema: "plot",
                table: "frame_placements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "mark",
                schema: "plot",
                table: "frame_placements",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "depth",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "mark",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "depth",
                schema: "plot",
                table: "frame_props");

            migrationBuilder.DropColumn(
                name: "mark",
                schema: "plot",
                table: "frame_props");

            migrationBuilder.DropColumn(
                name: "screen",
                schema: "plot",
                table: "frame_props");

            migrationBuilder.DropColumn(
                name: "depth",
                schema: "plot",
                table: "frame_placements");

            migrationBuilder.DropColumn(
                name: "mark",
                schema: "plot",
                table: "frame_placements");

            migrationBuilder.DropColumn(
                name: "depth",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.DropColumn(
                name: "mark",
                schema: "plot",
                table: "frame_extras");
        }
    }
}
