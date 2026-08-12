using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId_Status_CompletionDate",
                table: "Orders",
                columns: new[] { "OrganizationId", "Status", "CompletionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_OrderId_Date",
                table: "Expenses",
                columns: new[] { "OrganizationId", "OrderId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_OrganizationId_Status_CompletionDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_OrderId_Date",
                table: "Expenses");
        }
    }
}
