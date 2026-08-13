using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class DeploymentProfileTests
{
    [Fact]
    public void Production_Requires_Explicit_Mode_Except_Known_Public_Demo()
    {
        Assert.Throws<InvalidOperationException>(()=>DeploymentProfile.Create(new ConfigurationBuilder().Build(),new EnvironmentStub("Production")));
        var demo=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"PUBLIC_BASE_URL","https://seller-finance.onrender.com"}}).Build();Assert.Equal(ApplicationMode.Demo,DeploymentProfile.Create(demo,new EnvironmentStub("Production")).Mode);
    }

    [Fact]
    public void Demo_Seed_Cannot_Be_Enabled_For_Pilot()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"APP_MODE","Pilot"},{"SEED_DEMO_DATA","true"}}).Build();Assert.Throws<InvalidOperationException>(()=>DeploymentProfile.Create(config,new EnvironmentStub("Production")));
    }

    [Fact]
    public async Task Database_Seed_Is_Opt_In()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);await DatabaseSeed.InitializeAsync(db);Assert.Empty(await db.Organizations.ToArrayAsync());await DatabaseSeed.InitializeAsync(db,true);Assert.Contains(await db.Organizations.ToArrayAsync(),x=>x.Id=="demo-organization");
    }

    private sealed class EnvironmentStub(string name):IHostEnvironment{public string EnvironmentName{get;set;}=name;public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}=".";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
