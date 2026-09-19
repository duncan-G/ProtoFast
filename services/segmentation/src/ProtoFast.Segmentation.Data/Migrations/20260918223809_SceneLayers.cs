using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Segmentation.Data.Migrations
{
    /// <inheritdoc />
    public partial class SceneLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_family_instincts_family_pattern",
                table: "family_instincts");

            // "unknown", not "": a run submitted before the composition family existed has an
            // unknown one, and the resolution in scene plan §6.1 treats that as the fallback it is.
            // An empty string would be a family name nothing recognises.
            migrationBuilder.AddColumn<string>(
                name: "composition_family",
                table: "runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            // Every instinct captured before the axis existed came from structure review, on the
            // composition axis — the two defaults place them exactly where they were already being
            // used, so no existing guidance changes which agent sees it.
            migrationBuilder.AddColumn<string>(
                name: "axis",
                table: "family_instincts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Composition");

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "family_instincts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Structure");

            migrationBuilder.CreateIndex(
                name: "ix_family_instincts_family_axis_scope_pattern",
                table: "family_instincts",
                columns: new[] { "family", "axis", "scope", "pattern" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_family_instincts_family_axis_scope_pattern",
                table: "family_instincts");

            migrationBuilder.DropColumn(
                name: "composition_family",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "axis",
                table: "family_instincts");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "family_instincts");

            migrationBuilder.CreateIndex(
                name: "ix_family_instincts_family_pattern",
                table: "family_instincts",
                columns: new[] { "family", "pattern" },
                unique: true);
        }
    }
}
