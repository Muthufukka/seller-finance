using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProductCostHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalculationDateFallback",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CompletionDate",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    FileNameSafe = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    MatchedRows = table.Column<int>(type: "integer", nullable: false),
                    UnmatchedRows = table.Column<int>(type: "integer", nullable: false),
                    ErrorRows = table.Column<int>(type: "integer", nullable: false),
                    DuplicateRows = table.Column<int>(type: "integer", nullable: false),
                    ExpectedChanges = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostImportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CostImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: true),
                    CostAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostImportRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductCostHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCostHistory", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ProductCostHistory" ("Id", "OrganizationId", "ProductId", "CostAmount", "EffectiveFrom", "Source", "CreatedByUserId", "CreatedAt")
                SELECT md5(random()::text || clock_timestamp()::text)::uuid, "OrganizationId", "Id", "CurrentCost", DATE '1900-01-01', 3, 'migration', NOW()
                FROM "Products" WHERE "CurrentCost" IS NOT NULL;
                UPDATE "Orders" SET "CompletionDate" = "Date" WHERE "Status" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CostImportRows_ImportJobId_RowNumber",
                table: "CostImportRows",
                columns: new[] { "ImportJobId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostHistory_OrganizationId_ProductId_EffectiveFrom",
                table: "ProductCostHistory",
                columns: new[] { "OrganizationId", "ProductId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostImportJobs");

            migrationBuilder.DropTable(
                name: "CostImportRows");

            migrationBuilder.DropTable(
                name: "ProductCostHistory");

            migrationBuilder.DropColumn(
                name: "CalculationDateFallback",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CompletionDate",
                table: "Orders");
        }
    }
}
