using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTenantRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusHistory_Orders_OrderId",
                table: "OrderStatusHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_MarketplaceConnections_MarketplaceConnectionId",
                table: "SyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_OrderStatusHistory_OrderId",
                table: "OrderStatusHistory");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Products_Id_OrganizationId",
                table: "Products",
                columns: new[] { "Id", "OrganizationId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Orders_Id_OrganizationId",
                table: "Orders",
                columns: new[] { "Id", "OrganizationId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MarketplaceConnections_Id_OrganizationId",
                table: "MarketplaceConnections",
                columns: new[] { "Id", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId_OrganizationId",
                table: "SyncJobs",
                columns: new[] { "MarketplaceConnectionId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostHistory_ProductId_OrganizationId",
                table: "ProductCostHistory",
                columns: new[] { "ProductId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_OrderId_OrganizationId",
                table: "OrderStatusHistory",
                columns: new[] { "OrderId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_MarketplaceConnectionId_OrganizationId",
                table: "Orders",
                columns: new[] { "MarketplaceConnectionId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeeRules_ProductId_OrganizationId",
                table: "FeeRules",
                columns: new[] { "ProductId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrderId_OrganizationId",
                table: "Expenses",
                columns: new[] { "OrderId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ProductId_OrganizationId",
                table: "Expenses",
                columns: new[] { "ProductId", "OrganizationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CostImportRows_CostImportJobs_ImportJobId",
                table: "CostImportRows",
                column: "ImportJobId",
                principalTable: "CostImportJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Orders_OrderId_OrganizationId",
                table: "Expenses",
                columns: new[] { "OrderId", "OrganizationId" },
                principalTable: "Orders",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Products_ProductId_OrganizationId",
                table: "Expenses",
                columns: new[] { "ProductId", "OrganizationId" },
                principalTable: "Products",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FeeRules_Products_ProductId_OrganizationId",
                table: "FeeRules",
                columns: new[] { "ProductId", "OrganizationId" },
                principalTable: "Products",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId_Organ~",
                table: "Orders",
                columns: new[] { "MarketplaceConnectionId", "OrganizationId" },
                principalTable: "MarketplaceConnections",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusHistory_Orders_OrderId_OrganizationId",
                table: "OrderStatusHistory",
                columns: new[] { "OrderId", "OrganizationId" },
                principalTable: "Orders",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCostHistory_Products_ProductId_OrganizationId",
                table: "ProductCostHistory",
                columns: new[] { "ProductId", "OrganizationId" },
                principalTable: "Products",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_MarketplaceConnections_MarketplaceConnectionId_Org~",
                table: "SyncJobs",
                columns: new[] { "MarketplaceConnectionId", "OrganizationId" },
                principalTable: "MarketplaceConnections",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostImportRows_CostImportJobs_ImportJobId",
                table: "CostImportRows");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Orders_OrderId_OrganizationId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Products_ProductId_OrganizationId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_FeeRules_Products_ProductId_OrganizationId",
                table: "FeeRules");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId_Organ~",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusHistory_Orders_OrderId_OrganizationId",
                table: "OrderStatusHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCostHistory_Products_ProductId_OrganizationId",
                table: "ProductCostHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_MarketplaceConnections_MarketplaceConnectionId_Org~",
                table: "SyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_MarketplaceConnectionId_OrganizationId",
                table: "SyncJobs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Products_Id_OrganizationId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductCostHistory_ProductId_OrganizationId",
                table: "ProductCostHistory");

            migrationBuilder.DropIndex(
                name: "IX_OrderStatusHistory_OrderId_OrganizationId",
                table: "OrderStatusHistory");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Orders_Id_OrganizationId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_MarketplaceConnectionId_OrganizationId",
                table: "Orders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MarketplaceConnections_Id_OrganizationId",
                table: "MarketplaceConnections");

            migrationBuilder.DropIndex(
                name: "IX_FeeRules_ProductId_OrganizationId",
                table: "FeeRules");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrderId_OrganizationId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ProductId_OrganizationId",
                table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_OrderId",
                table: "OrderStatusHistory",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_MarketplaceConnections_MarketplaceConnectionId",
                table: "Orders",
                column: "MarketplaceConnectionId",
                principalTable: "MarketplaceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusHistory_Orders_OrderId",
                table: "OrderStatusHistory",
                column: "OrderId",
                principalTable: "Orders",
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
    }
}
