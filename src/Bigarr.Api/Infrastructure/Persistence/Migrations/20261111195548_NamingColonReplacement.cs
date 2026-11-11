using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NamingColonReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColonReplacement",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4); // Smart: what an existing install already did to a colon followed by a space

            migrationBuilder.AddColumn<bool>(
                name: "ReplaceIllegalCharacters",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesColonReplacement",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "SeriesCustomColonReplacement",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SeriesReplaceIllegalCharacters",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColonReplacement",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "ReplaceIllegalCharacters",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeriesColonReplacement",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeriesCustomColonReplacement",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeriesReplaceIllegalCharacters",
                table: "MediaManagement");
        }
    }
}
