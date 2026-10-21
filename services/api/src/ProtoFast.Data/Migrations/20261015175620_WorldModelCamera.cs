using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorldModelCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_location_observations_camera_columns",
                schema: "plot",
                table: "location_observations",
                sql: "edge <> 'Camera' OR (place_location_id IS NOT NULL AND object_id IS NULL AND character_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_location_observations_camera_columns",
                schema: "plot",
                table: "location_observations");
        }
    }
}
