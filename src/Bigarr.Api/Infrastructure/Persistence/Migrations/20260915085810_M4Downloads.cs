using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M4Downloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Implementation = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    UrlBase = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Password = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AddPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadClients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Grabs_DownloadClientId",
                table: "Grabs",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadClients_Name",
                table: "DownloadClients",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Grabs_DownloadClients_DownloadClientId",
                table: "Grabs",
                column: "DownloadClientId",
                principalTable: "DownloadClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Grabs_DownloadClients_DownloadClientId",
                table: "Grabs");

            migrationBuilder.DropTable(
                name: "DownloadClients");

            migrationBuilder.DropIndex(
                name: "IX_Grabs_DownloadClientId",
                table: "Grabs");
        }
    }
}
