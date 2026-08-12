using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class KaspiOrderEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "OrderLines",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId_ExternalId",
                table: "OrderLines",
                columns: new[] { "OrderId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderLines_OrderId_ExternalId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "OrderLines");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines",
                column: "OrderId");
        }
    }
}
