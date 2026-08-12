using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class OrganizationSettingsTests
{
    [Fact]
    public async Task Owner_Can_Update_Name_And_TimeZone()
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="Old"});await db.SaveChangesAsync();
        var result=await OrganizationSettings.UpdateAsync(db,"org",new("org",OrganizationRole.Owner),"Aspan Shop","Asia/Qyzylorda","kzt");
        Assert.Equal(OrganizationSettingsFailure.None,result.Failure);Assert.Equal("Aspan Shop",result.Organization!.Name);Assert.Equal("Asia/Qyzylorda",result.Organization.TimeZone);Assert.Equal("KZT",result.Organization.Currency);
    }

    [Theory]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task Read_Only_Roles_Cannot_Update_Organization(OrganizationRole role)
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="Original"});await db.SaveChangesAsync();
        var result=await OrganizationSettings.UpdateAsync(db,"org",new("org",role),"Changed","Asia/Almaty","KZT");
        Assert.Equal(OrganizationSettingsFailure.Forbidden,result.Failure);Assert.Equal("Original",(await db.Organizations.SingleAsync()).Name);
    }

    [Fact]
    public async Task Cross_Tenant_Update_Is_Forbidden()
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org-b",Name="Tenant B"});await db.SaveChangesAsync();
        var result=await OrganizationSettings.UpdateAsync(db,"org-b",new("org-a",OrganizationRole.Owner),"Stolen","Asia/Almaty","KZT");
        Assert.Equal(OrganizationSettingsFailure.Forbidden,result.Failure);Assert.Equal("Tenant B",(await db.Organizations.SingleAsync()).Name);
    }

    [Theory]
    [InlineData("X","Asia/Almaty","KZT",OrganizationSettingsFailure.InvalidName)]
    [InlineData("Valid name","Mars/Olympus","KZT",OrganizationSettingsFailure.InvalidTimeZone)]
    [InlineData("Valid name","Asia/Almaty","USD",OrganizationSettingsFailure.InvalidCurrency)]
    public async Task Invalid_Settings_Are_Rejected(string name,string zone,string currency,OrganizationSettingsFailure expected)
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="Original"});await db.SaveChangesAsync();
        var result=await OrganizationSettings.UpdateAsync(db,"org",new("org",OrganizationRole.Admin),name,zone,currency);
        Assert.Equal(expected,result.Failure);Assert.Equal("Original",(await db.Organizations.SingleAsync()).Name);
    }

    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
