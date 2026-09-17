using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M6Automation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SearchAttempts",
                table: "AcquisitionTargets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NotificationEndpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Events = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJobs",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSucceeded = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastSummary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobs", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<long>(type: "INTEGER", nullable: false),
                    EndpointId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_NotificationEndpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "NotificationEndpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_OutboxEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "OutboxEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEndpoints_Name",
                table: "NotificationEndpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_DispatchedAt",
                table: "OutboxEvents",
                column: "DispatchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_DeliveredAt_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "DeliveredAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EndpointId_CreatedAt",
                table: "WebhookDeliveries",
                columns: new[] { "EndpointId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EventId",
                table: "WebhookDeliveries",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledJobs");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationEndpoints");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "SearchAttempts",
                table: "AcquisitionTargets");
        }
    }
}
