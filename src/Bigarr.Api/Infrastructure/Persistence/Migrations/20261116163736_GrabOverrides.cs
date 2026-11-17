using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrabOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EpisodesOverridden",
                table: "Grabs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LanguagesOverride",
                table: "Grabs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityOverride",
                table: "Grabs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpisodesOverridden",
                table: "Grabs");

            migrationBuilder.DropColumn(
                name: "LanguagesOverride",
                table: "Grabs");

            migrationBuilder.DropColumn(
                name: "QualityOverride",
                table: "Grabs");
        }
    }
}
