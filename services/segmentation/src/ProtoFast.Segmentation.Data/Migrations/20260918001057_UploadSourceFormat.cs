using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Segmentation.Data.Migrations
{
    /// <inheritdoc />
    public partial class UploadSourceFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "media_type",
                table: "uploads",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "requires_conversion",
                table: "uploads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "source_extension",
                table: "uploads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "media_type",
                table: "uploads");

            migrationBuilder.DropColumn(
                name: "requires_conversion",
                table: "uploads");

            migrationBuilder.DropColumn(
                name: "source_extension",
                table: "uploads");
        }
    }
}
