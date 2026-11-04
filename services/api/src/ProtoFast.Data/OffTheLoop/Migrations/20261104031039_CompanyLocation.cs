using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.OffTheLoop.Migrations
{
    /// <inheritdoc />
    public partial class CompanyLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "loop",
                table: "companies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location",
                schema: "loop",
                table: "companies");
        }
    }
}
