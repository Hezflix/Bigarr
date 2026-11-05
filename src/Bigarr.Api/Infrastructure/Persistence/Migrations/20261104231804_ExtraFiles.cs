using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtraFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The existing settings row gets Radarr's defaults: extras on, the usual subtitle formats.
            migrationBuilder.AddColumn<string>(
                name: "ExtraFileExtensions",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "srt,ass,ssa,sub,idx,sup,vtt");

            migrationBuilder.AddColumn<bool>(
                name: "ImportExtraFiles",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ExtraFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Forced = table.Column<bool>(type: "INTEGER", nullable: false),
                    HearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtraFiles_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtraFiles_MediaFileId",
                table: "ExtraFiles",
                column: "MediaFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtraFiles");

            migrationBuilder.DropColumn(
                name: "ExtraFileExtensions",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "ImportExtraFiles",
                table: "MediaManagement");
        }
    }
}
