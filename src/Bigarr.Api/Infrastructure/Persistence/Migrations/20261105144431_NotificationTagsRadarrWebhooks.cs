using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationTagsRadarrWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VersionId",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: true);

            // Existing endpoints keep what they had: Bigarr's own body (WebhookFormat.Bigarr = 1), default view.
            migrationBuilder.AddColumn<int>(
                name: "Format",
                table: "NotificationEndpoints",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "View",
                table: "NotificationEndpoints",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.CreateTable(
                name: "NotificationEndpointTags",
                columns: table => new
                {
                    EndpointId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEndpointTags", x => new { x.EndpointId, x.TagId });
                    table.ForeignKey(
                        name: "FK_NotificationEndpointTags_NotificationEndpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "NotificationEndpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationEndpointTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEndpointTags_TagId",
                table: "NotificationEndpointTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationEndpointTags");

            migrationBuilder.DropColumn(
                name: "VersionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "NotificationEndpoints");

            migrationBuilder.DropColumn(
                name: "View",
                table: "NotificationEndpoints");
        }
    }
}
