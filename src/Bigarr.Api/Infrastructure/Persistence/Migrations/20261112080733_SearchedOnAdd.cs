using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchedOnAdd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SearchedOnAddAt",
                table: "SeriesVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SearchedOnAddAt",
                table: "MovieVersions",
                type: "TEXT",
                nullable: true);

            // Every version that exists today is past its search on add (done, or deliberately never due): only versions
            // created from now on start unsearched, so the upgrade searches nothing again.
            migrationBuilder.Sql("UPDATE \"SeriesVersions\" SET \"SearchedOnAddAt\" = \"CreatedAt\";");
            migrationBuilder.Sql("UPDATE \"MovieVersions\" SET \"SearchedOnAddAt\" = (SELECT \"CreatedAt\" FROM \"AcquisitionTargets\" WHERE \"AcquisitionTargets\".\"Id\" = \"MovieVersions\".\"Id\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchedOnAddAt",
                table: "SeriesVersions");

            migrationBuilder.DropColumn(
                name: "SearchedOnAddAt",
                table: "MovieVersions");
        }
    }
}
