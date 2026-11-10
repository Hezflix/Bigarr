using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesSizesAndProfileKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<double>(
                name: "SeriesMaxSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SeriesMinSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SeriesPreferredSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true);

            // Existing profiles came from Radarr or are the film system profiles: Films. One only series versions use is
            // Series, one both kinds use is Both, so no version is left with a profile of the wrong kind.
            migrationBuilder.Sql("UPDATE QualityProfiles SET Kind = 2 WHERE Id IN (SELECT QualityProfileId FROM SeriesVersions) AND Id NOT IN (SELECT QualityProfileId FROM MovieVersions);");
            migrationBuilder.Sql("UPDATE QualityProfiles SET Kind = 3 WHERE Id IN (SELECT QualityProfileId FROM SeriesVersions) AND Id IN (SELECT QualityProfileId FROM MovieVersions);");

            // Series sizes start from Sonarr's defaults (Features/Quality/SonarrDefaultSizes.cs); qualities Sonarr lacks take its Unknown limits.
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 1, SeriesPreferredSize = 95, SeriesMaxSize = 199.9;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 1, SeriesPreferredSize = 95, SeriesMaxSize = 199.9 WHERE Id = 0;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 1;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 12;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 8;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 2;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 20;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 2, SeriesPreferredSize = 95, SeriesMaxSize = 100 WHERE Id = 21;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 3, SeriesPreferredSize = 95, SeriesMaxSize = 125 WHERE Id = 4;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = 125 WHERE Id = 9;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 10;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 3, SeriesPreferredSize = 95, SeriesMaxSize = 130 WHERE Id = 14;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 3, SeriesPreferredSize = 95, SeriesMaxSize = 130 WHERE Id = 5;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = 130 WHERE Id = 6;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = 130 WHERE Id = 15;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = 130 WHERE Id = 3;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 4, SeriesPreferredSize = 95, SeriesMaxSize = 155 WHERE Id = 7;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 30;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = 199.9 WHERE Id = 16;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 17;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 18;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 19;");
            migrationBuilder.Sql("UPDATE QualityDefinitions SET SeriesMinSize = 35, SeriesPreferredSize = 95, SeriesMaxSize = NULL WHERE Id = 31;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "SeriesMaxSize",
                table: "QualityDefinitions");

            migrationBuilder.DropColumn(
                name: "SeriesMinSize",
                table: "QualityDefinitions");

            migrationBuilder.DropColumn(
                name: "SeriesPreferredSize",
                table: "QualityDefinitions");
        }
    }
}
