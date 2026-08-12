using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs",
                column: "MarketplaceConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MarketplaceConnections_Organizations_OrganizationId",
                table: "MarketplaceConnections",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId",
                table: "Orders",
                column: "MarketplaceConnectionId",
                principalTable: "MarketplaceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationFeatureFlags_Organizations_OrganizationId",
                table: "OrganizationFeatureFlags",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Organizations_OrganizationId",
                table: "Subscriptions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_MarketplaceConnections_MarketplaceConnectionId",
                table: "SyncJobs",
                column: "MarketplaceConnectionId",
                principalTable: "MarketplaceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarketplaceConnections_Organizations_OrganizationId",
                table: "MarketplaceConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationFeatureFlags_Organizations_OrganizationId",
                table: "OrganizationFeatureFlags");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Organizations_OrganizationId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_MarketplaceConnections_MarketplaceConnectionId",
                table: "SyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId",
                table: "SyncJobs");
        }
    }
}
