using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class ViewPictureStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_view_pictures_view",
                schema: "plot",
                table: "view_pictures");

            migrationBuilder.AddColumn<int>(
                name: "state",
                schema: "plot",
                table: "view_pictures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_view",
                schema: "plot",
                table: "view_pictures",
                columns: new[] { "scene_id", "camera_location_id", "camera_part", "toward_location_id", "toward_part", "state" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_view_pictures_state_non_negative",
                schema: "plot",
                table: "view_pictures",
                sql: "state >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_view_pictures_view",
                schema: "plot",
                table: "view_pictures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_view_pictures_state_non_negative",
                schema: "plot",
                table: "view_pictures");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "plot",
                table: "view_pictures");

            migrationBuilder.CreateIndex(
                name: "ix_view_pictures_view",
                schema: "plot",
                table: "view_pictures",
                columns: new[] { "scene_id", "camera_location_id", "camera_part", "toward_location_id", "toward_part" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
