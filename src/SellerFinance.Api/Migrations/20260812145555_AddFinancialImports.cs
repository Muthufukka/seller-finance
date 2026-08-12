using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportFingerprint",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                table: "ActualFees",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "ActualFees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinancialImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FileNameSafe = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    UpdateRows = table.Column<int>(type: "integer", nullable: false),
                    DuplicateRows = table.Column<int>(type: "integer", nullable: false),
                    ErrorRows = table.Column<int>(type: "integer", nullable: false),
                    ExpectedChanges = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialImportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    ExpenseType = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    ProductId = table.Column<string>(type: "text", nullable: true),
                    OrderId = table.Column<string>(type: "text", nullable: true),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    ExternalRef = table.Column<string>(type: "text", nullable: true),
                    Fingerprint = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialImportRows_FinancialImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "FinancialImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_ImportFingerprint",
                table: "Expenses",
                columns: new[] { "OrganizationId", "ImportFingerprint" },
                unique: true,
                filter: "\"ImportFingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportJobs_OrganizationId_CreatedAt",
                table: "FinancialImportJobs",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportRows_ImportJobId_RowNumber",
                table: "FinancialImportRows",
                columns: new[] { "ImportJobId", "RowNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialImportRows");

            migrationBuilder.DropTable(
                name: "FinancialImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_ImportFingerprint",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ImportFingerprint",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                table: "ActualFees");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "ActualFees");
        }
    }
}
