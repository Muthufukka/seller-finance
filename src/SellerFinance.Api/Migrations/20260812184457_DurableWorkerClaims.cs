using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class DurableWorkerClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "MarketplaceConnectionId" ORDER BY "CreatedAt", "Id") AS row_number
                    FROM "SyncJobs"
                    WHERE "Status" IN (0, 1, 3)
                )
                UPDATE "SyncJobs" AS job
                SET "Status" = 4,
                    "CompletedAt" = NOW(),
                    "ErrorCode" = COALESCE(job."ErrorCode", 'DUPLICATE_ACTIVE_JOB_MIGRATION')
                FROM ranked
                WHERE job."Id" = ranked."Id" AND ranked.row_number > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "NotificationDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "ExportJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs",
                column: "MarketplaceConnectionId",
                unique: true,
                filter: "\"Status\" IN (0, 1, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ExportJobs");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs",
                column: "MarketplaceConnectionId");
        }
    }
}
