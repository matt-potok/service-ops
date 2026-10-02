using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceOps.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioDemoMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoSeedStates",
                columns: table => new
                {
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AnchorUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoSeedStates", x => x.Version);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoSeedStates");
        }
    }
}
