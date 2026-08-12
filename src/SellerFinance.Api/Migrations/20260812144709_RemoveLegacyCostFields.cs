using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SellerFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyCostFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "ProductCostHistory" ("Id", "OrganizationId", "ProductId", "CostAmount", "EffectiveFrom", "Source", "CreatedByUserId", "CreatedAt")
                SELECT md5(p."OrganizationId" || ':' || p."Id" || ':legacy-current-cost')::uuid,
                       p."OrganizationId", p."Id", p."CurrentCost", DATE '1900-01-01', 3, 'migration', NOW()
                FROM "Products" p
                WHERE p."CurrentCost" IS NOT NULL
                ON CONFLICT ("OrganizationId", "ProductId", "EffectiveFrom") DO NOTHING;

                INSERT INTO "ProductCostHistory" ("Id", "OrganizationId", "ProductId", "CostAmount", "EffectiveFrom", "Source", "CreatedByUserId", "CreatedAt")
                SELECT md5(o."OrganizationId" || ':' || l."ProductId" || ':' || COALESCE(o."CompletionDate", o."Date")::text || ':legacy-line-cost')::uuid,
                       o."OrganizationId", l."ProductId", MAX(l."UnitCost"), COALESCE(o."CompletionDate", o."Date"), 3, 'migration', NOW()
                FROM "OrderLines" l
                JOIN "Orders" o ON o."Id" = l."OrderId"
                WHERE l."UnitCost" IS NOT NULL
                GROUP BY o."OrganizationId", l."ProductId", COALESCE(o."CompletionDate", o."Date")
                ON CONFLICT ("OrganizationId", "ProductId", "EffectiveFrom") DO NOTHING;
                """);

            migrationBuilder.DropColumn(
                name: "CurrentCost",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "OrderLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentCost",
                table: "Products",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "OrderLines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Products" p SET "CurrentCost" = (
                    SELECT h."CostAmount" FROM "ProductCostHistory" h
                    WHERE h."OrganizationId" = p."OrganizationId" AND h."ProductId" = p."Id" AND h."EffectiveFrom" <= CURRENT_DATE
                    ORDER BY h."EffectiveFrom" DESC LIMIT 1
                );

                UPDATE "OrderLines" l SET "UnitCost" = (
                    SELECT h."CostAmount" FROM "Orders" o JOIN "ProductCostHistory" h
                      ON h."OrganizationId" = o."OrganizationId" AND h."ProductId" = l."ProductId"
                    WHERE h."OrganizationId" = o."OrganizationId" AND h."ProductId" = l."ProductId" AND h."EffectiveFrom" <= COALESCE(o."CompletionDate", o."Date")
                      AND o."Id" = l."OrderId"
                    ORDER BY h."EffectiveFrom" DESC LIMIT 1
                );
                """);
        }
    }
}
