using Microsoft.EntityFrameworkCore;
using Npgsql;
using SellerFinance.Api;

namespace SellerFinance.Tests;

[Collection("PostgreSQL")]
public sealed class PostgresIntegrationTests
{
    private static string? ConnectionString=>Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");

    [Fact]
    public async Task Migrations_Apply_To_Empty_PostgreSql_And_Enforce_Tenant_Cost_Uniqueness()
    {
        if(ConnectionString is null)return;
        await ResetDatabaseAsync();await using var db=CreateDb();await db.Database.MigrateAsync();
        var migrations=await db.Database.GetAppliedMigrationsAsync();Assert.Contains("20260812184457_DurableWorkerClaims",migrations);
        db.Organizations.Add(new(){Id="pg-org",Name="Postgres"});db.Products.Add(new(){Id="pg-product",OrganizationId="pg-org",Sku="PG",Name="Product"});await db.SaveChangesAsync();
        db.ProductCostHistory.AddRange(Cost(Guid.NewGuid()),Cost(Guid.NewGuid()));
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal(PostgresErrorCodes.UniqueViolation,((PostgresException)error.InnerException!).SqlState);
    }

    [Fact]
    public async Task Concurrent_Cost_Confirm_Is_Applied_Exactly_Once_On_PostgreSql()
    {
        if(ConnectionString is null)return;
        await ResetDatabaseAsync();Guid jobId=Guid.NewGuid();await using(var setup=CreateDb()){await setup.Database.MigrateAsync();setup.Organizations.Add(new(){Id="org",Name="Org"});setup.Products.Add(new(){Id="product",OrganizationId="org",Sku="SKU",Name="Product"});setup.CostImportJobs.Add(new(){Id=jobId,OrganizationId="org",CreatedByUserId="user",FileNameSafe="costs.csv",Source=CostSource.CsvImport,TotalRows=1,MatchedRows=1,ExpectedChanges=1});setup.CostImportRows.Add(new(){Id=Guid.NewGuid(),ImportJobId=jobId,RowNumber=2,Sku="SKU",ProductId="product",CostAmount=100,EffectiveFrom=new(2026,8,1)});await setup.SaveChangesAsync();}
        async Task<bool> Confirm(){await using var db=CreateDb();try{await new CostImportService(db).ConfirmAsync(jobId,"org","user",default);return true;}catch(CostImportException){return false;}}
        var results=await Task.WhenAll(Confirm(),Confirm());Assert.Single(results,x=>x);await using var verify=CreateDb();Assert.Equal(1,await verify.ProductCostHistory.CountAsync());Assert.Equal(CostImportStatus.Applied,(await verify.CostImportJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Concurrent_Financial_Confirm_Is_Applied_Exactly_Once_On_PostgreSql()
    {
        if(ConnectionString is null)return;await ResetDatabaseAsync();var jobId=Guid.NewGuid();await using(var setup=CreateDb()){await setup.Database.MigrateAsync();setup.Organizations.Add(new(){Id="org",Name="Org"});setup.FinancialImportJobs.Add(new(){Id=jobId,OrganizationId="org",CreatedByUserId="user",Type=FinancialImportType.Expenses,FileNameSafe="expenses.csv",TotalRows=1,ValidRows=1,ExpectedChanges=1});setup.FinancialImportRows.Add(new(){Id=Guid.NewGuid(),ImportJobId=jobId,RowNumber=2,Status="Valid",ExpenseType=ExpenseType.Other,Amount=100,Date=new(2026,8,1),Fingerprint="CONCURRENT-FINGERPRINT"});await setup.SaveChangesAsync();}
        async Task<bool> Confirm(){await using var db=CreateDb();try{await new FinancialImportService(db).ConfirmAsync(jobId,"org","user",default);return true;}catch(FinancialImportException){return false;}}var results=await Task.WhenAll(Confirm(),Confirm());Assert.Single(results,x=>x);await using var verify=CreateDb();Assert.Equal(1,await verify.Expenses.CountAsync());Assert.Equal(FinancialImportStatus.Applied,(await verify.FinancialImportJobs.SingleAsync()).Status);
    }

    private static ProductCostHistoryEntity Cost(Guid id)=>new(){Id=id,OrganizationId="pg-org",ProductId="pg-product",CostAmount=100,EffectiveFrom=new(2026,8,1),CreatedByUserId="user"};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseNpgsql(ConnectionString!).Options);
    private static async Task ResetDatabaseAsync(){await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;",connection);await command.ExecuteNonQueryAsync();}
}

[CollectionDefinition("PostgreSQL",DisableParallelization=true)]
public sealed class PostgresCollection;
