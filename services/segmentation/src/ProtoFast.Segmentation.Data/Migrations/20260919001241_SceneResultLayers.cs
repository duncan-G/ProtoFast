using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Segmentation.Data.Migrations
{
    /// <inheritdoc />
    public partial class SceneResultLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "items_json",
                table: "run_results",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "registries_json",
                table: "run_results",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "scenes_json",
                table: "run_results",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "items_json",
                table: "run_results");

            migrationBuilder.DropColumn(
                name: "registries_json",
                table: "run_results");

            migrationBuilder.DropColumn(
                name: "scenes_json",
                table: "run_results");
        }
    }
}
