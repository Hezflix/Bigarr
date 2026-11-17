using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonGroupAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KeepReleaseGroups",
                table: "SeriesVersionSeasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AlignSeasonGroups",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AlignSeasonGroupsSince",
                table: "MediaManagement",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeepReleaseGroups",
                table: "SeriesVersionSeasons");

            migrationBuilder.DropColumn(
                name: "AlignSeasonGroups",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "AlignSeasonGroupsSince",
                table: "MediaManagement");
        }
    }
}
