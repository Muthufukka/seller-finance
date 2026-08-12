using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionsAndMultiStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_OrganizationId_ExternalId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceConnections_OrganizationId_Provider",
                table: "MarketplaceConnections");

            migrationBuilder.AddColumn<Guid>(
                name: "MarketplaceConnectionId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "MarketplaceConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    Plan = table.Column<int>(type: "integer", nullable: false),
                    BillingPeriod = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrialEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Subscriptions" ("Id", "OrganizationId", "Plan", "BillingPeriod", "Status", "PeriodStart", "PeriodEnd", "TrialEndsAt", "UpdatedAt")
                SELECT md5(o."Id" || ':subscription')::uuid, o."Id", o."Plan", 'Monthly',
                       CASE WHEN o."Plan" = 0 THEN 0 ELSE 1 END,
                       COALESCE(o."CreatedAt", NOW()),
                       CASE WHEN o."Plan" = 0 THEN o."TrialEndsAt" ELSE NOW() + INTERVAL '1 month' END,
                       CASE WHEN o."Plan" = 0 THEN o."TrialEndsAt" ELSE NULL END,
                       NOW()
                FROM "Organizations" o;

                UPDATE "MarketplaceConnections"
                SET "DisplayName" = CASE WHEN "Provider" = 'Kaspi' THEN 'Kaspi Магазин' ELSE "Provider" END;

                INSERT INTO "MarketplaceConnections" ("Id", "OrganizationId", "Provider", "DisplayName", "TokenCiphertext", "TokenNonce", "TokenTag", "Status", "CreatedAt", "UpdatedAt")
                SELECT md5(o."OrganizationId" || ':legacy-connection')::uuid, o."OrganizationId", 'Kaspi', 'Исторические данные',
                       decode('', 'hex'), decode('', 'hex'), decode('', 'hex'), 3, NOW(), NOW()
                FROM (SELECT DISTINCT "OrganizationId" FROM "Orders") o
                WHERE NOT EXISTS (SELECT 1 FROM "MarketplaceConnections" c WHERE c."OrganizationId" = o."OrganizationId");

                UPDATE "Orders" o
                SET "MarketplaceConnectionId" = (
                    SELECT c."Id" FROM "MarketplaceConnections" c
                    WHERE c."OrganizationId" = o."OrganizationId"
                    ORDER BY c."CreatedAt", c."Id" LIMIT 1
                );
                """);

            migrationBuilder.DropColumn(name: "Plan", table: "Organizations");
            migrationBuilder.DropColumn(name: "TrialEndsAt", table: "Organizations");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_MarketplaceConnectionId_ExternalId",
                table: "Orders",
                columns: new[] { "MarketplaceConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId_Date",
                table: "Orders",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConnections_OrganizationId_Provider_DisplayName",
                table: "MarketplaceConnections",
                columns: new[] { "OrganizationId", "Provider", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_OrganizationId",
                table: "Subscriptions",
                column: "OrganizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_MarketplaceConnectionId_ExternalId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrganizationId_Date",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceConnections_OrganizationId_Provider_DisplayName",
                table: "MarketplaceConnections");

            migrationBuilder.DropColumn(
                name: "MarketplaceConnectionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "MarketplaceConnections");

            migrationBuilder.AddColumn<int>(
                name: "Plan",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndsAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE "Organizations" o
                SET "Plan" = s."Plan", "TrialEndsAt" = COALESCE(s."TrialEndsAt", s."PeriodEnd")
                FROM "Subscriptions" s WHERE s."OrganizationId" = o."Id";
                """);

            migrationBuilder.DropTable(name: "Subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId_ExternalId",
                table: "Orders",
                columns: new[] { "OrganizationId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConnections_OrganizationId_Provider",
                table: "MarketplaceConnections",
                columns: new[] { "OrganizationId", "Provider" },
                unique: true);
        }
    }
}
