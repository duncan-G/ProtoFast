using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorldModelHeldObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_location_observations_at_columns",
                schema: "plot",
                table: "location_observations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_location_observations_at_columns",
                schema: "plot",
                table: "location_observations",
                sql: "edge <> 'At' OR (object_id IS NOT NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL AND (character_id IS NULL OR place_location_id IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_location_observations_at_columns",
                schema: "plot",
                table: "location_observations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_location_observations_at_columns",
                schema: "plot",
                table: "location_observations",
                sql: "edge <> 'At' OR (object_id IS NOT NULL AND character_id IS NULL AND other_location_id IS NULL AND other_part_id IS NULL AND via_object_id IS NULL AND property IS NULL AND value IS NULL)");
        }
    }
}
