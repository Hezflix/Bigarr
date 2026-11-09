using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonPackUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeasonPackUpgrade",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SeasonPackUpgradeThreshold",
                table: "MediaManagement",
                type: "REAL",
                nullable: false,
                defaultValue: 100.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeasonPackUpgrade",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeasonPackUpgradeThreshold",
                table: "MediaManagement");
        }
    }
}
