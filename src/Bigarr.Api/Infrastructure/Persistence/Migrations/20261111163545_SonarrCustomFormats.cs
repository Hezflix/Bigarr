using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SonarrCustomFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormatMap",
                table: "FollowedSonarrs",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "OwnedFormats",
                table: "FollowedSonarrs",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "CustomFormats",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormatMap",
                table: "FollowedSonarrs");

            migrationBuilder.DropColumn(
                name: "OwnedFormats",
                table: "FollowedSonarrs");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "CustomFormats");
        }
    }
}
