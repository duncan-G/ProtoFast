using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePanelsToFrames : Migration
    {
        private const string Schema = "plot";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Rename(migrationBuilder, "panel", "frame");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Rename(migrationBuilder, "frame", "panel");

        // Renames in place so existing rows survive; EF would otherwise drop and recreate the tables.
        private static void Rename(MigrationBuilder migrationBuilder, string from, string to)
        {
            string Name(string pattern) => pattern.Replace("{0}", from);
            string NewName(string pattern) => pattern.Replace("{0}", to);

            foreach (var table in new[] { "{0}s", "{0}_placements", "{0}_props" })
            {
                migrationBuilder.RenameTable(name: Name(table), schema: Schema, newName: NewName(table), newSchema: Schema);
            }

            foreach (var table in new[] { "{0}_placements", "{0}_props" })
            {
                migrationBuilder.RenameColumn(name: Name("{0}_id"), schema: Schema, table: NewName(table), newName: NewName("{0}_id"));
            }

            var constraints = new[]
            {
                ("{0}s", "pk_{0}s"),
                ("{0}s", "ck_{0}s_hold_ms"),
                ("{0}s", "ck_{0}s_position_non_negative"),
                ("{0}s", "fk_{0}s_locations_location_id"),
                ("{0}s", "fk_{0}s_scene_elements_first_element_id"),
                ("{0}s", "fk_{0}s_scene_elements_last_element_id"),
                ("{0}s", "fk_{0}s_scenes_scene_id"),
                ("{0}_placements", "pk_{0}_placements"),
                ("{0}_placements", "fk_{0}_placements_character_wardrobes_wardrobe_id"),
                ("{0}_placements", "fk_{0}_placements_characters_character_id"),
                ("{0}_placements", "fk_{0}_placements_{0}s_{0}_id"),
                ("{0}_props", "pk_{0}_props"),
                ("{0}_props", "fk_{0}_props_{0}s_{0}_id"),
                ("{0}_props", "fk_{0}_props_props_prop_id"),
            };
            foreach (var (table, constraint) in constraints)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {Schema}.{NewName(table)} RENAME CONSTRAINT {Name(constraint)} TO {NewName(constraint)};");
            }

            var indexes = new[]
            {
                ("{0}s", "ix_{0}s_first_element_id"),
                ("{0}s", "ix_{0}s_last_element_id"),
                ("{0}s", "ix_{0}s_location_id"),
                ("{0}s", "ix_{0}s_scene_id_position"),
                ("{0}_placements", "ix_{0}_placements_character_id"),
                ("{0}_placements", "ix_{0}_placements_wardrobe_id"),
                ("{0}_props", "ix_{0}_props_prop_id"),
            };
            foreach (var (table, index) in indexes)
            {
                migrationBuilder.RenameIndex(name: Name(index), schema: Schema, table: NewName(table), newName: NewName(index));
            }
        }
    }
}
