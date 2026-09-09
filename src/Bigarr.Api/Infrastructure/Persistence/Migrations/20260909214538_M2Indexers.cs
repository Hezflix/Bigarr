using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2Indexers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Indexers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Implementation = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ApiPath = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableRss = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableAutomaticSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableInteractiveSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumSeeders = table.Column<int>(type: "INTEGER", nullable: false),
                    SeedRatio = table.Column<double>(type: "REAL", nullable: true),
                    SeedTimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    RateLimitSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indexers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndexerTags",
                columns: table => new
                {
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerTags", x => new { x.IndexerId, x.TagId });
                    table.ForeignKey(
                        name: "FK_IndexerTags_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndexerTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_History_IndexerId",
                table: "History",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_Grabs_IndexerId",
                table: "Grabs",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_IndexerId",
                table: "Blocklist",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_Indexers_Name",
                table: "Indexers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerTags_TagId",
                table: "IndexerTags",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_Blocklist_Indexers_IndexerId",
                table: "Blocklist",
                column: "IndexerId",
                principalTable: "Indexers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Grabs_Indexers_IndexerId",
                table: "Grabs",
                column: "IndexerId",
                principalTable: "Indexers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_History_Indexers_IndexerId",
                table: "History",
                column: "IndexerId",
                principalTable: "Indexers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Blocklist_Indexers_IndexerId",
                table: "Blocklist");

            migrationBuilder.DropForeignKey(
                name: "FK_Grabs_Indexers_IndexerId",
                table: "Grabs");

            migrationBuilder.DropForeignKey(
                name: "FK_History_Indexers_IndexerId",
                table: "History");

            migrationBuilder.DropTable(
                name: "IndexerTags");

            migrationBuilder.DropTable(
                name: "Indexers");

            migrationBuilder.DropIndex(
                name: "IX_History_IndexerId",
                table: "History");

            migrationBuilder.DropIndex(
                name: "IX_Grabs_IndexerId",
                table: "Grabs");

            migrationBuilder.DropIndex(
                name: "IX_Blocklist_IndexerId",
                table: "Blocklist");
        }
    }
}
