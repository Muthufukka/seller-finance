using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class FinancialImportTests
{
    [Fact]
    public async Task Expense_Import_Confirms_Valid_Row_And_Detects_Reimport()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();var service=new FinancialImportService(db);
        var csv="Type;Amount;Date;SKU;Comment\nAdvertising;1250,50;2026-08-01;SKU-1;Campaign";
        var first=await service.PreviewAsync(FinancialImportType.Expenses,"org","user",File(csv,"expenses.csv"),default);
        Assert.Equal(1,await service.ConfirmAsync(first.Id,"org","user",default));
        var expense=await db.Expenses.SingleAsync();Assert.Equal(1250.50m,expense.Amount);Assert.Equal(ExpenseSource.Import,expense.Source);Assert.Equal("p1",expense.ProductId);
        var second=await service.PreviewAsync(FinancialImportType.Expenses,"org","user",File(csv,"expenses.csv"),default);
        Assert.Equal(1,second.DuplicateRows);Assert.Equal(0,second.ExpectedChanges);
    }

    [Fact]
    public async Task Expense_Import_Does_Not_Resolve_Product_From_Other_Tenant()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="foreign",OrganizationId="other",Sku="PRIVATE",Name="Private"});await db.SaveChangesAsync();
        var job=await new FinancialImportService(db).PreviewAsync(FinancialImportType.Expenses,"org","user",File("Type,Amount,Date,SKU\nOther,100,2026-08-01,PRIVATE","expenses.csv"),default);
        Assert.Equal(1,job.ErrorRows);Assert.Equal(0,job.ExpectedChanges);Assert.Null((await db.FinancialImportRows.SingleAsync()).ProductId);
    }

    [Fact]
    public async Task Actual_Fee_Import_Is_Tenant_Scoped_And_Upserts()
    {
        await using var db=CreateDb();var line=Guid.NewGuid();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.Orders.Add(new(){Id="o1",OrganizationId="org",ExternalId="ORDER-1",Date=new(2026,8,1),Lines=[new(){Id=line,OrderId="o1",ExternalId="LINE-1",ProductId="p1"}]});db.ActualFees.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",OrderLineId=line,Amount=100,CreatedByUserId="user"});await db.SaveChangesAsync();var service=new FinancialImportService(db);
        var job=await service.PreviewAsync(FinancialImportType.ActualFees,"org","user",File("OrderExternalId,LineExternalId,Amount,ExternalRef\nORDER-1,LINE-1,150,REPORT-1","fees.csv"),default);
        Assert.Equal(1,job.UpdateRows);Assert.Equal(1,await service.ConfirmAsync(job.Id,"org","user",default));var fee=await db.ActualFees.SingleAsync();Assert.Equal(150,fee.Amount);Assert.Equal("Import",fee.Source);Assert.Equal("REPORT-1",fee.ExternalRef);
        var foreign=await service.PreviewAsync(FinancialImportType.ActualFees,"other","user",File("OrderExternalId,LineExternalId,Amount\nORDER-1,LINE-1,500","fees.csv"),default);Assert.Equal(1,foreign.ErrorRows);
    }

    private static FormFile File(string content,string name){var bytes=Encoding.UTF8.GetBytes(content);return new FormFile(new MemoryStream(bytes),0,bytes.Length,"file",name);}
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
