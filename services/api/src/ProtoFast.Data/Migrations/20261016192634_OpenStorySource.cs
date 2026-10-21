using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProtoFast.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpenStorySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_story_id",
                schema: "plot",
                table: "stories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stories_user_id_source_story_id",
                schema: "plot",
                table: "stories",
                columns: new[] { "user_id", "source_story_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stories_user_id_source_story_id",
                schema: "plot",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "source_story_id",
                schema: "plot",
                table: "stories");
        }
    }
}
