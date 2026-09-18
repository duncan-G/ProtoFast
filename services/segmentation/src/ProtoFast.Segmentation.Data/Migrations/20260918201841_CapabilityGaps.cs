using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProtoFast.Segmentation.Data.Migrations
{
    /// <inheritdoc />
    public partial class CapabilityGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capability_gaps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    proposal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    document_family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capability_gaps", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_capability_gaps_kind_created_at",
                table: "capability_gaps",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_capability_gaps_run_id",
                table: "capability_gaps",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capability_gaps");
        }
    }
}
