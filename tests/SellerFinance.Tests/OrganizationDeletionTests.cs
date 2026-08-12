using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SellerFinance.Api;
using SellerFinance.Domain;

namespace SellerFinance.Tests;

public sealed class OrganizationDeletionTests
{
    [Fact]
    public void Delete_Route_Metadata_Can_Be_Built()
    {
        var builder=WebApplication.CreateBuilder();builder.Services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(Guid.NewGuid().ToString()));builder.Services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<SellerFinanceDbContext>().AddSignInManager();
        var app=builder.Build();
#pragma warning disable AD0001
        app.MapDelete("/organizations/{id}",OrganizationEndpoints.DeleteAsync);
#pragma warning restore AD0001
        var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(x=>x.Endpoints).ToArray();
        Assert.Single(endpoints);Assert.Contains("DELETE",endpoints[0].DisplayName);
    }

    [Fact]
    public async Task Owner_Deletion_Removes_All_Tenant_Data_And_Orphaned_Account()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var line=Guid.NewGuid();var import=Guid.NewGuid();
        db.Users.Add(new(){Id="owner",UserName="owner@example.test",Email="owner@example.test",DisplayName="Owner"});
        db.Organizations.Add(new(){Id="org",Name="Delete Me"});db.OrganizationUsers.Add(new(){OrganizationId="org",UserId="owner",Role=OrganizationRole.Owner,JoinedAt=DateTimeOffset.UtcNow});
        db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId="org"});db.Products.Add(new(){Id="p",OrganizationId="org",Sku="SKU",Name="Product"});
        db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org",DisplayName="Shop"});
        db.Orders.Add(new(){Id="o",OrganizationId="org",MarketplaceConnectionId=connection,ExternalId="1",Date=new(2026,8,12),Status=OrderStatus.Completed,Lines=[new(){Id=line,OrderId="o",ProductId="p",Revenue=100,Quantity=1}]});
        db.ActualFees.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",OrderLineId=line,Amount=10,CreatedByUserId="owner"});
        db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p",CostAmount=50,EffectiveFrom=new(2026,8,1),CreatedByUserId="owner"});
        db.CostImportJobs.Add(new(){Id=import,OrganizationId="org",CreatedByUserId="owner"});db.CostImportRows.Add(new(){Id=Guid.NewGuid(),ImportJobId=import,RowNumber=1,Sku="SKU"});
        db.FeeRules.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Value=10,EffectiveFrom=new(2026,8,1),CreatedByUserId="owner"});db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Amount=20,Date=new(2026,8,1),CreatedByUserId="owner"});
        db.ExportJobs.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",CreatedByUserId="owner",DownloadTokenHash="download"});db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",LinkCodeHash="link"});
        db.NotificationRules.Add(new(){Id=Guid.NewGuid(),OrganizationId="org"});db.NotificationDeliveries.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",DeduplicationKey="dedupe",Message="safe"});
        db.OrganizationFeatureFlags.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Key="KaspiSync",UpdatedByUserId="owner"});db.OrganizationInvitations.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Email="member@example.test",TokenHash="invite",InvitedByUserId="owner"});db.AuditLogs.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",UserId="owner",Action="old",EntityType="Organization"});await db.SaveChangesAsync();

        var result=await OrganizationDeletion.DeleteAsync(db,"org","owner",new("org",OrganizationRole.Owner),"Delete Me");

        Assert.Equal(OrganizationDeletionFailure.None,result.Failure);Assert.True(result.UserDeleted);Assert.Equal(1,result.DeletedOrders);Assert.Equal(1,result.DeletedProducts);
        Assert.False(await db.Organizations.AnyAsync());Assert.False(await db.Users.AnyAsync());Assert.False(await HasAnyTenantData(db,"org"));
        var retained=await db.AuditLogs.SingleAsync();Assert.Null(retained.OrganizationId);Assert.Null(retained.UserId);Assert.Equal("privacy.organization.deleted",retained.Action);Assert.DoesNotContain("Delete Me",retained.MetadataSafe);
    }

    [Fact]
    public async Task Account_Is_Preserved_When_User_Has_Another_Membership()
    {
        await using var db=CreateDb();db.Users.Add(new(){Id="owner",UserName="owner@example.test"});db.Organizations.AddRange(new(){Id="a",Name="Delete A"},new(){Id="b",Name="Keep B"});db.OrganizationUsers.AddRange(new(){OrganizationId="a",UserId="owner",Role=OrganizationRole.Owner,JoinedAt=DateTimeOffset.UtcNow},new(){OrganizationId="b",UserId="owner",Role=OrganizationRole.Viewer,JoinedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
        var result=await OrganizationDeletion.DeleteAsync(db,"a","owner",new("a",OrganizationRole.Owner),"Delete A");
        Assert.False(result.UserDeleted);Assert.True(await db.Users.AnyAsync(x=>x.Id=="owner"));Assert.True(await db.Organizations.AnyAsync(x=>x.Id=="b"));Assert.True(await db.OrganizationUsers.AnyAsync(x=>x.OrganizationId=="b"));
    }

    [Theory]
    [InlineData(OrganizationRole.Admin)]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task Only_Owner_Can_Delete(OrganizationRole role)
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="Org"});await db.SaveChangesAsync();
        Assert.Equal(OrganizationDeletionFailure.Forbidden,(await OrganizationDeletion.DeleteAsync(db,"org","user",new("org",role),"Org")).Failure);Assert.True(await db.Organizations.AnyAsync());
    }

    [Fact]
    public async Task Cross_Tenant_And_Wrong_Confirmation_Are_Rejected()
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="b",Name="Tenant B"});await db.SaveChangesAsync();
        Assert.Equal(OrganizationDeletionFailure.Forbidden,(await OrganizationDeletion.DeleteAsync(db,"b","owner",new("a",OrganizationRole.Owner),"Tenant B")).Failure);
        Assert.Equal(OrganizationDeletionFailure.ConfirmationMismatch,(await OrganizationDeletion.DeleteAsync(db,"b","owner",new("b",OrganizationRole.Owner),"tenant b")).Failure);Assert.True(await db.Organizations.AnyAsync());
    }

    [Fact]
    public async Task Active_Sync_Blocks_Deletion()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();db.Organizations.Add(new(){Id="org",Name="Org"});db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org"});db.SyncJobs.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.Running});await db.SaveChangesAsync();
        Assert.Equal(OrganizationDeletionFailure.ActiveSync,(await OrganizationDeletion.DeleteAsync(db,"org","owner",new("org",OrganizationRole.Owner),"Org")).Failure);Assert.True(await db.Organizations.AnyAsync());
    }

    private static async Task<bool> HasAnyTenantData(SellerFinanceDbContext db,string org)=>await db.Products.AnyAsync(x=>x.OrganizationId==org)||await db.Orders.AnyAsync(x=>x.OrganizationId==org)||await db.ActualFees.AnyAsync(x=>x.OrganizationId==org)||await db.CostImportJobs.AnyAsync(x=>x.OrganizationId==org)||await db.FeeRules.AnyAsync(x=>x.OrganizationId==org)||await db.Expenses.AnyAsync(x=>x.OrganizationId==org)||await db.ExportJobs.AnyAsync(x=>x.OrganizationId==org)||await db.TelegramConnections.AnyAsync(x=>x.OrganizationId==org)||await db.NotificationRules.AnyAsync(x=>x.OrganizationId==org)||await db.NotificationDeliveries.AnyAsync(x=>x.OrganizationId==org)||await db.OrganizationFeatureFlags.AnyAsync(x=>x.OrganizationId==org)||await db.OrganizationInvitations.AnyAsync(x=>x.OrganizationId==org)||await db.OrganizationUsers.AnyAsync(x=>x.OrganizationId==org);
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
