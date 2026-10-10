using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class FrameStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "travel",
                schema: "plot",
                table: "scene_appearances",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "camera",
                schema: "plot",
                table: "frames",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "facing",
                schema: "plot",
                table: "frame_placements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "looking",
                schema: "plot",
                table: "frame_placements",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "moving",
                schema: "plot",
                table: "frame_placements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<bool>(
                name: "on_screen",
                schema: "plot",
                table: "frame_extras",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<string>(
                name: "facing",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "looking",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "moving",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "pose",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "screen",
                schema: "plot",
                table: "frame_extras",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "travel",
                schema: "plot",
                table: "scene_appearances");

            migrationBuilder.DropColumn(
                name: "camera",
                schema: "plot",
                table: "frames");

            migrationBuilder.DropColumn(
                name: "facing",
                schema: "plot",
                table: "frame_placements");

            migrationBuilder.DropColumn(
                name: "looking",
                schema: "plot",
                table: "frame_placements");

            migrationBuilder.DropColumn(
                name: "moving",
                schema: "plot",
                table: "frame_placements");

            migrationBuilder.DropColumn(
                name: "facing",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.DropColumn(
                name: "looking",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.DropColumn(
                name: "moving",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.DropColumn(
                name: "pose",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.DropColumn(
                name: "screen",
                schema: "plot",
                table: "frame_extras");

            migrationBuilder.AlterColumn<bool>(
                name: "on_screen",
                schema: "plot",
                table: "frame_extras",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }
    }
}
