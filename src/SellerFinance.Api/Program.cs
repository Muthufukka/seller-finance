using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

var builder = WebApplication.CreateBuilder(args);
var connection = DatabaseConfiguration.GetConnectionString(builder.Configuration)
    ?? throw new InvalidOperationException("DATABASE_URL is required.");
builder.Services.AddDbContext<SellerFinanceDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = false; // SMTP provider is configured in the next delivery slice.
    o.Password.RequiredLength = 10;
    o.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<SellerFinanceDbContext>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "seller_finance_session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton<TokenCipher>();
builder.Services.AddHttpClient<KaspiClient>(client =>
{
    client.BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/");
    client.Timeout=TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<KaspiSyncWorker>();
builder.Services.AddScoped<CostImportService>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await DatabaseSeed.InitializeAsync(scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>());

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status="healthy", service="SellerFinance.Api" }));
app.MapGet("/health/database", async (SellerFinanceDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status="healthy", provider="PostgreSQL" })
    : Results.Problem("Database connection failed", statusCode:503));

var auth = app.MapGroup("/api/v1/auth");
auth.MapPost("/register", async (RegisterRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, SellerFinanceDbContext db) =>
{
    if (String.IsNullOrWhiteSpace(request.OrganizationName) || request.OrganizationName.Trim().Length < 2)
        return Results.BadRequest(new { title="Укажите название организации" });
    var user = new AppUser { UserName=request.Email.Trim().ToLowerInvariant(), Email=request.Email.Trim().ToLowerInvariant(), DisplayName=request.DisplayName.Trim(), EmailConfirmed=true };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(x=>x.Code).ToDictionary(x=>x.Key,x=>x.Select(y=>y.Description).ToArray()));
    var organization = new OrganizationEntity { Id=Guid.NewGuid().ToString("N"), Name=request.OrganizationName.Trim() };
    db.Organizations.Add(organization);
    db.OrganizationUsers.Add(new() { OrganizationId=organization.Id, UserId=user.Id, Role=OrganizationRole.Owner, JoinedAt=DateTimeOffset.UtcNow });
    db.AuditLogs.Add(new() { Id=Guid.NewGuid(), OrganizationId=organization.Id, UserId=user.Id, Action="organization.created", EntityType="Organization", EntityId=organization.Id });
    await db.SaveChangesAsync();
    await signIn.SignInAsync(user, isPersistent:true);
    return Results.Ok(new { organizationId=organization.Id, organizationName=organization.Name, userName=user.DisplayName, role="Owner" });
});
auth.MapPost("/login", async (LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, SellerFinanceDbContext db) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is null || (await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure:true)).Succeeded is false)
        return Results.Problem("Неверный email или пароль", statusCode:401);
    db.AuditLogs.Add(new() { Id=Guid.NewGuid(), UserId=user.Id, Action="auth.login", EntityType="User", EntityId=user.Id });
    await db.SaveChangesAsync();
    return Results.Ok(new { status="authenticated" });
});
auth.MapPost("/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, UserManager<AppUser> users) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is not null) _ = await users.GeneratePasswordResetTokenAsync(user);
    return Results.Ok(new { message="Если аккаунт существует, инструкция будет отправлена на email." });
});
auth.MapPost("/reset-password", async (ResetPasswordRequest request, UserManager<AppUser> users) =>
{
    var user=await users.FindByEmailAsync(request.Email.Trim());
    if (user is null) return Results.BadRequest(new { title="Недействительный запрос" });
    var result=await users.ResetPasswordAsync(user,request.Token,request.NewPassword);
    return result.Succeeded ? Results.NoContent() : Results.ValidationProblem(result.Errors.GroupBy(x=>x.Code).ToDictionary(x=>x.Key,x=>x.Select(y=>y.Description).ToArray()));
});

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.AddEndpointFilter(async (invocation, next) =>
{
    var context=invocation.HttpContext;
    if (context.Request.Path.StartsWithSegments("/api/v1/auth")) return await next(invocation);
    var db=context.RequestServices.GetRequiredService<SellerFinanceDbContext>();
    var membership=await TenantSecurity.ResolveAsync(context,db);
    if (membership is null) return Results.NotFound();
    context.Items["membership"]=membership;
    return await next(invocation);
});
api.MapGet("/session", async (HttpContext ctx, SellerFinanceDbContext db) =>
{
    var organization=await db.Organizations.AsNoTracking().SingleAsync(x=>x.Id==ctx.Tenant());
    var user=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
    return Results.Ok(new { userId=user.Id, email=user.Email, displayName=user.DisplayName, organizationId=organization.Id, organizationName=organization.Name, role=ctx.Membership().Role.ToString(), plan="Trial" });
});
api.MapGet("/organizations", async (ClaimsPrincipal user, SellerFinanceDbContext db) =>
{
    var userId=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    return Results.Ok(await (from m in db.OrganizationUsers.AsNoTracking() join o in db.Organizations on m.OrganizationId equals o.Id where m.UserId==userId&&m.JoinedAt!=null select new { o.Id,o.Name,role=m.Role.ToString() }).ToArrayAsync());
});
api.MapPost("/organizations/{id}/members", async (HttpContext ctx, string id, InviteMemberRequest request, SellerFinanceDbContext db) =>
{
    if (id!=ctx.Tenant()) return Results.NotFound();
    if (!ctx.Membership().CanManageMembers()) return Results.Forbid();
    if (!Enum.TryParse<OrganizationRole>(request.Role,true,out var role) || role==OrganizationRole.Owner) return Results.BadRequest(new { title="Недопустимая роль" });
    var token=TokenTools.CreateToken();
    db.OrganizationInvitations.Add(new() { Id=Guid.NewGuid(), OrganizationId=id, Email=request.Email.Trim().ToLowerInvariant(), Role=role, TokenHash=TokenTools.Hash(token), InvitedByUserId=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!, ExpiresAt=DateTimeOffset.UtcNow.AddDays(7) });
    AuditWriter.Add(db,ctx,"member.invited","OrganizationInvitation",metadataSafe:$"{{\"role\":\"{role}\"}}");
    await db.SaveChangesAsync();
    return Results.Ok(new { invitationToken=token, expiresInDays=7 });
});
api.MapPost("/invitations/accept", async (HttpContext ctx, AcceptInvitationRequest request, SellerFinanceDbContext db) =>
{
    var invitation=await db.OrganizationInvitations.SingleOrDefaultAsync(x=>x.TokenHash==TokenTools.Hash(request.Token)&&x.AcceptedAt==null&&x.ExpiresAt>DateTimeOffset.UtcNow);
    if (invitation is null) return Results.BadRequest(new { title="Приглашение недействительно или истекло" });
    var userId=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var user=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId);
    if (!String.Equals(user.Email,invitation.Email,StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
    var membership=await db.OrganizationUsers.SingleOrDefaultAsync(x=>x.OrganizationId==invitation.OrganizationId&&x.UserId==userId);
    if (membership is null) db.OrganizationUsers.Add(new() { OrganizationId=invitation.OrganizationId, UserId=userId, Role=invitation.Role, InvitedAt=DateTimeOffset.UtcNow, JoinedAt=DateTimeOffset.UtcNow });
    else { membership.Role=invitation.Role; membership.JoinedAt=DateTimeOffset.UtcNow; }
    invitation.AcceptedAt=DateTimeOffset.UtcNow;
    AuditWriter.Add(db,ctx,"member.invitation.accepted","OrganizationInvitation",invitation.Id.ToString());
    await db.SaveChangesAsync();
    return Results.NoContent();
});
api.MapGet("/analytics/summary", async (HttpContext c, SellerFinanceDbContext db) => Results.Ok(await DbAnalytics.SummaryAsync(db,c.Tenant())));
api.MapGet("/analytics/timeseries", async (HttpContext c, SellerFinanceDbContext db) => Results.Ok(await DbAnalytics.TimeSeriesAsync(db,c.Tenant())));
api.MapGet("/analytics/products", async (HttpContext c, SellerFinanceDbContext db) => Results.Ok(await DbAnalytics.ProductsAsync(db,c.Tenant())));
api.MapGet("/orders", async (HttpContext c, SellerFinanceDbContext db) => Results.Ok(await DbAnalytics.OrdersAsync(db,c.Tenant())));
api.MapGet("/kaspi/connection", async (HttpContext ctx,SellerFinanceDbContext db) =>
{
    var connection=await db.MarketplaceConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi");
    if(connection is null)return Results.Ok(new { connected=false });
    var lastJob=await db.SyncJobs.AsNoTracking().Where(x=>x.MarketplaceConnectionId==connection.Id).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync();
    return Results.Ok(new { connected=true,status=connection.Status.ToString(),connection.LastVerifiedAt,connection.LastSuccessfulSyncAt,connection.LastErrorCode,lastJob=lastJob is null?null:new {lastJob.Id,status=lastJob.Status.ToString(),lastJob.ImportedOrders,lastJob.ErrorCode,lastJob.CreatedAt} });
});
api.MapPost("/kaspi/connection", async (HttpContext ctx,KaspiConnectionRequest request,SellerFinanceDbContext db,TokenCipher cipher,KaspiClient kaspi,CancellationToken ct) =>
{
    if(!ctx.Membership().CanManageMembers())return Results.Forbid();
    if(String.IsNullOrWhiteSpace(request.Token))return Results.BadRequest(new {title="Укажите API-токен Kaspi"});
    KaspiResult verification;
    try { verification=await kaspi.GetOrdersAsync(request.Token.Trim(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,ct); }
    catch(HttpRequestException) { return Results.Problem("Kaspi API временно недоступен",statusCode:503); }
    if(!verification.Success)return Results.Problem(verification.ErrorCode,statusCode:(int)verification.StatusCode);
    var encrypted=cipher.Encrypt(request.Token.Trim());
    var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi");
    if(connection is null){connection=new(){Id=Guid.NewGuid(),OrganizationId=ctx.Tenant()};db.MarketplaceConnections.Add(connection);}
    connection.TokenCiphertext=encrypted.Ciphertext;connection.TokenNonce=encrypted.Nonce;connection.TokenTag=encrypted.Tag;connection.Status=MarketplaceConnectionStatus.Active;connection.LastVerifiedAt=DateTimeOffset.UtcNow;connection.LastErrorCode=null;connection.UpdatedAt=DateTimeOffset.UtcNow;
    AuditWriter.Add(db,ctx,"integration.connected","MarketplaceConnection",connection.Id.ToString(),"{\"provider\":\"Kaspi\"}");
    await db.SaveChangesAsync(ct);
    return Results.Ok(new {connected=true,status="Active"});
});
api.MapPost("/kaspi/verify", async (HttpContext ctx,SellerFinanceDbContext db,TokenCipher cipher,KaspiClient kaspi,CancellationToken ct) =>
{
    var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi",ct);
    if(connection is null)return Results.NotFound();
    var result=await kaspi.GetOrdersAsync(cipher.Decrypt(connection),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,ct);
    connection.Status=result.Success?MarketplaceConnectionStatus.Active:MarketplaceConnectionStatus.RequiresAttention;connection.LastVerifiedAt=result.Success?DateTimeOffset.UtcNow:null;connection.LastErrorCode=result.ErrorCode;await db.SaveChangesAsync(ct);
    return result.Success?Results.Ok(new {status="Active"}):Results.Problem(result.ErrorCode,statusCode:(int)result.StatusCode);
});
api.MapPost("/kaspi/sync", async (HttpContext ctx,SellerFinanceDbContext db,CancellationToken ct) =>
{
    if(!ctx.Membership().CanWrite())return Results.Forbid();
    var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi",ct);
    if(connection is null)return Results.NotFound();
    if(await db.SyncJobs.AnyAsync(x=>x.MarketplaceConnectionId==connection.Id&&(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.Running||x.Status==SyncJobStatus.RetryScheduled),ct))return Results.Conflict(new {title="Синхронизация уже выполняется"});
    var job=new SyncJobEntity{Id=Guid.NewGuid(),OrganizationId=ctx.Tenant(),MarketplaceConnectionId=connection.Id,WindowFrom=DateTimeOffset.UtcNow.AddDays(-30),WindowTo=DateTimeOffset.UtcNow};db.SyncJobs.Add(job);AuditWriter.Add(db,ctx,"integration.sync.queued","SyncJob",job.Id.ToString());await db.SaveChangesAsync(ct);
    return Results.Accepted($"/api/v1/kaspi/sync/{job.Id}",new {job.Id,status=job.Status.ToString()});
});
api.MapGet("/kaspi/sync/{id:guid}",async(HttpContext ctx,Guid id,SellerFinanceDbContext db)=>{var job=await db.SyncJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant());return job is null?Results.NotFound():Results.Ok(new{job.Id,status=job.Status.ToString(),job.Attempt,job.ImportedOrders,job.ErrorCode,job.CreatedAt,job.CompletedAt});});
api.MapPost("/products/{id}/costs", async (HttpContext ctx,string id,ProductCostRequest request,SellerFinanceDbContext db) =>
{
    if (!ctx.Membership().CanWrite()) return Results.Forbid();
    if(request.Cost<=0) return Results.BadRequest(new { title="Себестоимость должна быть больше нуля" });
    var product=await db.Products.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant());
    if(product is null) return Results.NotFound();
    var effective=request.EffectiveFrom??DateOnly.FromDateTime(DateTime.UtcNow);
    if(await db.ProductCostHistory.AnyAsync(x=>x.OrganizationId==ctx.Tenant()&&x.ProductId==id&&x.EffectiveFrom==effective))return Results.Conflict(new {title="Себестоимость на эту дату уже существует"});
    db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId=ctx.Tenant(),ProductId=id,CostAmount=request.Cost,EffectiveFrom=effective,Source=CostSource.Manual,CreatedByUserId=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!});
    AuditWriter.Add(db,ctx,"product.cost.changed","Product",id);
    await db.SaveChangesAsync();
    return Results.Ok(new { productId=id,cost=request.Cost,effectiveFrom=effective });
});
api.MapGet("/products/{id}",async(HttpContext ctx,string id,SellerFinanceDbContext db)=>
{
    var product=await db.Products.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant());if(product is null)return Results.NotFound();
    var history=await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==ctx.Tenant()&&x.ProductId==id).OrderByDescending(x=>x.EffectiveFrom).Select(x=>new{x.Id,x.CostAmount,x.EffectiveFrom,source=x.Source.ToString(),x.CreatedAt}).ToArrayAsync();
    return Results.Ok(new{product.Id,product.Sku,product.Name,currentCost=history.FirstOrDefault(x=>x.EffectiveFrom<=DateOnly.FromDateTime(DateTime.UtcNow))?.CostAmount,costHistory=history});
});
api.MapGet("/products/{id}/costs",async(HttpContext ctx,string id,SellerFinanceDbContext db)=>Results.Ok(await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==ctx.Tenant()&&x.ProductId==id).OrderByDescending(x=>x.EffectiveFrom).Select(x=>new{x.Id,x.CostAmount,x.EffectiveFrom,source=x.Source.ToString(),x.CreatedAt}).ToArrayAsync()));
api.MapPost("/costs/imports/preview",async(HttpContext ctx,IFormFile file,CostImportService imports,SellerFinanceDbContext db,CancellationToken ct)=>
{
    if(!ctx.Membership().CanWrite())return Results.Forbid();
    try{var job=await imports.PreviewAsync(ctx.Tenant(),ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!,file,ct);var rows=await db.CostImportRows.AsNoTracking().Where(x=>x.ImportJobId==job.Id).OrderBy(x=>x.RowNumber).Take(200).ToArrayAsync(ct);AuditWriter.Add(db,ctx,"product.cost.import.previewed","CostImportJob",job.Id.ToString());await db.SaveChangesAsync(ct);return Results.Ok(CostImportService.ToPreview(job,rows));}
    catch(CostImportException ex){return Results.BadRequest(new{title=ex.Message});}
}).DisableAntiforgery();
api.MapGet("/costs/imports/{id:guid}",async(HttpContext ctx,Guid id,SellerFinanceDbContext db)=>{var job=await db.CostImportJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant());if(job is null)return Results.NotFound();var rows=await db.CostImportRows.AsNoTracking().Where(x=>x.ImportJobId==id).OrderBy(x=>x.RowNumber).Take(200).ToArrayAsync();return Results.Ok(CostImportService.ToPreview(job,rows));});
api.MapPost("/costs/imports/{id:guid}/confirm",async(HttpContext ctx,Guid id,CostImportService imports,SellerFinanceDbContext db,CancellationToken ct)=>{if(!ctx.Membership().CanWrite())return Results.Forbid();try{var applied=await imports.ConfirmAsync(id,ctx.Tenant(),ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct);AuditWriter.Add(db,ctx,"product.cost.import.applied","CostImportJob",id.ToString(),$"{{\"appliedRows\":{applied}}}");await db.SaveChangesAsync(ct);return Results.Ok(new{appliedRows=applied});}catch(KeyNotFoundException){return Results.NotFound();}catch(CostImportException ex){return Results.Conflict(new{title=ex.Message});}});
api.MapGet("/costs/imports/template.xlsx",(HttpContext ctx)=>
{
    using var workbook=new ClosedXML.Excel.XLWorkbook();var sheet=workbook.AddWorksheet("Себестоимость");sheet.Cell("A1").Value="SKU";sheet.Cell("B1").Value="Cost";sheet.Cell("C1").Value="EffectiveFrom";sheet.Cell("A2").Value="EXAMPLE-001";sheet.Cell("B2").Value=1250.50;sheet.Cell("C2").Value=new DateTime(2026,8,1);sheet.Range("A1:C1").Style.Font.Bold=true;sheet.Column(1).Width=22;sheet.Column(2).Width=16;sheet.Column(3).Width=18;sheet.Column(2).Style.NumberFormat.Format="#,##0.00";sheet.Column(3).Style.DateFormat.Format="yyyy-mm-dd";using var stream=new MemoryStream();workbook.SaveAs(stream);return Results.File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","seller-finance-cost-import-template.xlsx");
});

app.MapFallbackToFile("index.html");
app.Run();

record RegisterRequest(string Email,string Password,string DisplayName,string OrganizationName);
record LoginRequest(string Email,string Password,bool RememberMe=true);
record ForgotPasswordRequest(string Email);
record ResetPasswordRequest(string Email,string Token,string NewPassword);
record InviteMemberRequest(string Email,string Role);
record AcceptInvitationRequest(string Token);
record ProductCostRequest(decimal Cost,DateOnly? EffectiveFrom);
record KaspiConnectionRequest(string Token);
public partial class Program { }
