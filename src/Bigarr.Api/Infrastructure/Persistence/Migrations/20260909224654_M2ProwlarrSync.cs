using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2ProwlarrSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProwlarrId",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Indexers_ProwlarrId",
                table: "Indexers",
                column: "ProwlarrId",
                unique: true,
                filter: "\"ProwlarrId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Indexers_ProwlarrId",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "ProwlarrId",
                table: "Indexers");
        }
    }
}
