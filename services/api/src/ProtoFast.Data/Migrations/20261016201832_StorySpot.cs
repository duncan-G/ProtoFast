using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorySpot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "spot_element_id",
                schema: "plot",
                table: "stories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "spot_scene_id",
                schema: "plot",
                table: "stories",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "spot_element_id",
                schema: "plot",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "spot_scene_id",
                schema: "plot",
                table: "stories");
        }
    }
}
