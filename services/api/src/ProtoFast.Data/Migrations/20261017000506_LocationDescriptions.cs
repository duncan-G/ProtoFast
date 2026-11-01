using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class LocationDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contents",
                schema: "plot",
                table: "locations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "plot",
                table: "locations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contents",
                schema: "plot",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "plot",
                table: "locations");
        }
    }
}
