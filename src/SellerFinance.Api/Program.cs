using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json;
using SellerFinance.Api;

var builder = WebApplication.CreateBuilder(args);
var connection = DatabaseConfiguration.GetConnectionString(builder.Configuration)
    ?? throw new InvalidOperationException("DATABASE_URL is required.");
builder.Services.AddDbContext<SellerFinanceDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = builder.Configuration.GetValue<bool>("EMAIL_CONFIRMATION_REQUIRED");
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
builder.Services.AddScoped<ExportBuilder>();
builder.Services.AddHostedService<ExportWorker>();
builder.Services.AddHttpClient<TelegramClient>(client=>client.Timeout=TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationDeliveryWorker>();
builder.Services.AddHostedService<SubscriptionMaintenanceWorker>();
builder.Services.AddRateLimiter(options=>
{
    options.AddFixedWindowLimiter("auth",o=>{o.PermitLimit=10;o.Window=TimeSpan.FromMinutes(1);o.QueueLimit=0;});
    options.AddFixedWindowLimiter("sensitive",o=>{o.PermitLimit=6;o.Window=TimeSpan.FromMinutes(1);o.QueueLimit=0;});
    options.RejectionStatusCode=429;
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<EmailDelivery>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await DatabaseSeed.InitializeAsync(scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>());

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async(context,next)=>{context.Response.Headers["X-Content-Type-Options"]="nosniff";context.Response.Headers["Referrer-Policy"]="strict-origin-when-cross-origin";context.Response.Headers["Content-Security-Policy"]="default-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'";await next();});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", (IConfiguration config) => Results.Ok(new { status="healthy", service="SellerFinance.Api", revision=config["RENDER_GIT_COMMIT"]?[..7] }));
app.MapGet("/health/database", async (SellerFinanceDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status="healthy", provider="PostgreSQL" })
    : Results.Problem("Database connection failed", statusCode:503));
app.MapGet("/health/ready",async(SellerFinanceDbContext db,IConfiguration config)=>await db.Database.CanConnectAsync()&&!String.IsNullOrWhiteSpace(config["TOKEN_ENCRYPTION_KEY"])?Results.Ok(new{status="ready",database="healthy",encryption="configured"}):Results.Problem("Service is not ready",statusCode:503));
if(app.Environment.IsDevelopment()||builder.Configuration.GetValue<bool>("ENABLE_OPENAPI"))app.MapOpenApi();
app.MapGet("/api/v1/exports/download/{token}",async(string token,SellerFinanceDbContext db)=>{var hash=TokenTools.Hash(token);var job=await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.DownloadTokenHash==hash&&x.Status==ExportJobStatus.Succeeded&&x.ExpiresAt>DateTimeOffset.UtcNow&&x.FileContent!=null);return job is null?Results.NotFound():Results.File(job.FileContent!,job.ContentType!,job.FileName);}).RequireRateLimiting("sensitive");
app.MapPost("/api/v1/telegram/webhook",async(HttpContext context,IConfiguration config,SellerFinanceDbContext db,TelegramClient telegram,CancellationToken ct)=>{var secret=context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();if(!TelegramWebhook.ValidSecret(secret,config))return Results.NotFound();var update=await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body,cancellationToken:ct);await TelegramWebhook.ProcessAsync(update,db,telegram,ct);return Results.Ok();}).RequireRateLimiting("sensitive");

var auth = app.MapGroup("/api/v1/auth");
auth.MapPost("/register", async (HttpContext context,RegisterRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, SellerFinanceDbContext db,EmailDelivery email,IConfiguration config) =>
{
    if (String.IsNullOrWhiteSpace(request.OrganizationName) || request.OrganizationName.Trim().Length < 2)
        return Results.BadRequest(new { title="Укажите название организации" });
    var confirmationRequired=config.GetValue<bool>("EMAIL_CONFIRMATION_REQUIRED");if(confirmationRequired&&!email.IsConfigured)return Results.Problem("Email delivery не настроен",statusCode:503);
    var user = new AppUser { UserName=request.Email.Trim().ToLowerInvariant(), Email=request.Email.Trim().ToLowerInvariant(), DisplayName=request.DisplayName.Trim(), EmailConfirmed=!confirmationRequired };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(x=>x.Code).ToDictionary(x=>x.Key,x=>x.Select(y=>y.Description).ToArray()));
    var organization = new OrganizationEntity { Id=Guid.NewGuid().ToString("N"), Name=request.OrganizationName.Trim() };
    db.Organizations.Add(organization);
    db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId=organization.Id});
    db.OrganizationUsers.Add(new() { OrganizationId=organization.Id, UserId=user.Id, Role=OrganizationRole.Owner, JoinedAt=DateTimeOffset.UtcNow });
    db.NotificationRules.AddRange(new NotificationRuleEntity{Id=Guid.NewGuid(),OrganizationId=organization.Id,EventType=NotificationEventType.SyncRequiresAttention,Enabled=true},new NotificationRuleEntity{Id=Guid.NewGuid(),OrganizationId=organization.Id,EventType=NotificationEventType.MissingCost,Enabled=true},new NotificationRuleEntity{Id=Guid.NewGuid(),OrganizationId=organization.Id,EventType=NotificationEventType.NegativeMargin,Enabled=true,Threshold=0m});
    db.AuditLogs.Add(new() { Id=Guid.NewGuid(), OrganizationId=organization.Id, UserId=user.Id, Action="organization.created", EntityType="Organization", EntityId=organization.Id });
    await db.SaveChangesAsync();
    if(confirmationRequired){var token=await users.GenerateEmailConfirmationTokenAsync(user);var url=$"{context.Request.Scheme}://{context.Request.Host}/api/v1/auth/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";if(!await email.SendAsync(user.Email!,"Подтверждение Seller Finance",EmailDelivery.ConfirmationHtml(url),context.RequestAborted))return Results.Problem("Не удалось отправить письмо подтверждения",statusCode:503);return Results.Ok(new{emailConfirmationRequired=true});}
    await signIn.SignInAsync(user, isPersistent:true);return Results.Ok(new { organizationId=organization.Id, organizationName=organization.Name, userName=user.DisplayName, role="Owner" });
}).RequireRateLimiting("auth");
auth.MapGet("/confirm-email",async(string userId,string token,UserManager<AppUser> users)=>{var user=await users.FindByIdAsync(userId);if(user is null)return Results.BadRequest("Недействительная ссылка");var result=await users.ConfirmEmailAsync(user,token);return result.Succeeded?Results.Content("Email подтверждён. Вернитесь в Seller Finance.","text/plain; charset=utf-8"):Results.BadRequest("Ссылка недействительна или истекла");}).RequireRateLimiting("auth");
auth.MapPost("/login", async (LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, SellerFinanceDbContext db) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is null || (await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure:true)).Succeeded is false)
    { db.AuditLogs.Add(new(){Id=Guid.NewGuid(),UserId=user?.Id,Action="auth.login.failed",EntityType="User",EntityId=user?.Id});await db.SaveChangesAsync();return Results.Problem("Неверный email или пароль", statusCode:401); }
    var now=DateTimeOffset.UtcNow;var activeOrganization=await(from membership in db.OrganizationUsers.AsNoTracking() join organization in db.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id join subscription in db.Subscriptions.AsNoTracking() on organization.Id equals subscription.OrganizationId where membership.UserId==user.Id&&membership.JoinedAt!=null&&organization.Status=="Active"&&(subscription.Status==SubscriptionStatus.Active||subscription.Status==SubscriptionStatus.Trialing)&&subscription.PeriodEnd>now select organization.Id).FirstOrDefaultAsync();if(activeOrganization is null){await signIn.SignOutAsync();db.AuditLogs.Add(new(){Id=Guid.NewGuid(),UserId=user.Id,Action="auth.login.blocked",EntityType="User",EntityId=user.Id});await db.SaveChangesAsync();return Results.Problem("Доступ к организации или подписке приостановлен. Обратитесь в поддержку.",statusCode:403);}
    db.AuditLogs.Add(new() { Id=Guid.NewGuid(), UserId=user.Id, Action="auth.login", EntityType="User", EntityId=user.Id });
    await db.SaveChangesAsync();
    return Results.Ok(new { status="authenticated" });
}).RequireRateLimiting("auth");
auth.MapPost("/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
auth.MapPost("/forgot-password", async (HttpContext context,ForgotPasswordRequest request, UserManager<AppUser> users,EmailDelivery email) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is not null&&email.IsConfigured){var token=await users.GeneratePasswordResetTokenAsync(user);var url=$"{context.Request.Scheme}://{context.Request.Host}/?resetEmail={Uri.EscapeDataString(user.Email!)}&resetToken={Uri.EscapeDataString(token)}";await email.SendAsync(user.Email!,"Сброс пароля Seller Finance",EmailDelivery.ResetHtml(url),context.RequestAborted);}
    return Results.Ok(new { message="Если аккаунт существует, инструкция будет отправлена на email." });
}).RequireRateLimiting("auth");
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
    var subscription=await Subscriptions.GetAsync(db,organization.Id);return Results.Ok(new { userId=user.Id, email=user.Email, displayName=user.DisplayName, organizationId=organization.Id, organizationName=organization.Name,organization.TimeZone,organization.Currency, role=ctx.Membership().Role.ToString(), plan=subscription.Plan.ToString(),subscriptionStatus=subscription.Status.ToString(),subscription.BillingPeriod,subscription.PeriodStart,subscription.PeriodEnd,subscription.TrialEndsAt,isSaasAdmin=SaasSecurity.IsAdmin(ctx.User,ctx.RequestServices.GetRequiredService<IConfiguration>()) });
});
api.MapGet("/organizations", async (ClaimsPrincipal user, SellerFinanceDbContext db) =>
{
    var userId=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    return Results.Ok(await (from m in db.OrganizationUsers.AsNoTracking() join o in db.Organizations on m.OrganizationId equals o.Id where m.UserId==userId&&m.JoinedAt!=null select new { o.Id,o.Name,role=m.Role.ToString() }).ToArrayAsync());
});
api.MapPut("/organizations/{id}",async(HttpContext ctx,string id,OrganizationSettingsRequest request,SellerFinanceDbContext db,CancellationToken ct)=>
{
    var result=await OrganizationSettings.UpdateAsync(db,id,ctx.Membership(),request.Name,request.TimeZone,request.Currency,ct);
    if(result.Failure==OrganizationSettingsFailure.NotFound)return Results.NotFound();
    if(result.Failure==OrganizationSettingsFailure.Forbidden)return Results.Forbid();
    if(result.Failure!=OrganizationSettingsFailure.None)return Results.BadRequest(new{title=result.Failure switch{OrganizationSettingsFailure.InvalidName=>"Название должно содержать от 2 до 120 символов",OrganizationSettingsFailure.InvalidTimeZone=>"Неизвестный часовой пояс IANA",_=>"В MVP поддерживается валюта KZT"}});
    AuditWriter.Add(db,ctx,"organization.settings.changed","Organization",id,JsonSerializer.Serialize(new{result.Organization!.Name,result.Organization.TimeZone,result.Organization.Currency}));await db.SaveChangesAsync(ct);return Results.Ok(new{result.Organization.Name,result.Organization.TimeZone,result.Organization.Currency});
}).RequireRateLimiting("sensitive");
api.MapDelete("/organizations/{id}",OrganizationEndpoints.DeleteAsync).RequireRateLimiting("sensitive");
api.MapGet("/organizations/{id}/members",async(HttpContext ctx,string id,SellerFinanceDbContext db)=>id!=ctx.Tenant()?Results.NotFound():Results.Ok(await(from m in db.OrganizationUsers.AsNoTracking() join u in db.Users on m.UserId equals u.Id where m.OrganizationId==id&&m.JoinedAt!=null select new{u.Id,u.Email,u.DisplayName,role=m.Role.ToString(),m.JoinedAt}).ToArrayAsync()));
api.MapPut("/organizations/{id}/members/{userId}/role",async(HttpContext ctx,string id,string userId,ChangeRoleRequest request,SellerFinanceDbContext db)=>{if(id!=ctx.Tenant())return Results.NotFound();if(!ctx.Membership().CanManageMembers())return Results.Forbid();if(!Enum.TryParse<OrganizationRole>(request.Role,true,out var role)||role==OrganizationRole.Owner)return Results.BadRequest(new{title="Недопустимая роль"});var membership=await db.OrganizationUsers.SingleOrDefaultAsync(x=>x.OrganizationId==id&&x.UserId==userId&&x.Role!=OrganizationRole.Owner);if(membership is null)return Results.NotFound();membership.Role=role;AuditWriter.Add(db,ctx,"member.role.changed","OrganizationUser",userId,$"{{\"role\":\"{role}\"}}");await db.SaveChangesAsync();return Results.NoContent();});
api.MapPost("/organizations/{id}/members", async (HttpContext ctx, string id, InviteMemberRequest request, SellerFinanceDbContext db) =>
{
    if (id!=ctx.Tenant()) return Results.NotFound();
    if (!ctx.Membership().CanManageMembers()) return Results.Forbid();
    if (!Enum.TryParse<OrganizationRole>(request.Role,true,out var role) || role==OrganizationRole.Owner) return Results.BadRequest(new { title="Недопустимая роль" });
    var subscription=await Subscriptions.GetAsync(db,id);var memberLimit=PlanLimits.MaxMembers(subscription.Plan);var members=await db.OrganizationUsers.CountAsync(x=>x.OrganizationId==id&&x.JoinedAt!=null);if(members>=memberLimit)return Results.Problem("Достигнут лимит пользователей тарифа",statusCode:402);
    var token=TokenTools.CreateToken();
    db.OrganizationInvitations.Add(new() { Id=Guid.NewGuid(), OrganizationId=id, Email=request.Email.Trim().ToLowerInvariant(), Role=role, TokenHash=TokenTools.Hash(token), InvitedByUserId=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!, ExpiresAt=DateTimeOffset.UtcNow.AddDays(7) });
    AuditWriter.Add(db,ctx,"member.invited","OrganizationInvitation",metadataSafe:$"{{\"role\":\"{role}\"}}");
    await db.SaveChangesAsync();
    return Results.Ok(new { invitationToken=token, expiresInDays=7 });
}).RequireRateLimiting("sensitive");
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
api.MapGet("/analytics/summary", async (HttpContext c, SellerFinanceDbContext db,DateOnly? dateFrom,DateOnly? dateTo) => Results.Ok(await DbAnalytics.SummaryAsync(db,c.Tenant(),dateFrom,dateTo)));
api.MapGet("/analytics/timeseries", async (HttpContext c, SellerFinanceDbContext db,DateOnly? dateFrom,DateOnly? dateTo) => Results.Ok(await DbAnalytics.TimeSeriesAsync(db,c.Tenant(),dateFrom,dateTo)));
api.MapGet("/analytics/products", async (HttpContext c, SellerFinanceDbContext db,DateOnly? dateFrom,DateOnly? dateTo) => Results.Ok(await DbAnalytics.ProductsAsync(db,c.Tenant(),dateFrom,dateTo)));
api.MapGet("/analytics/abc",async(HttpContext c,SellerFinanceDbContext db,string? metric,DateOnly? dateFrom,DateOnly? dateTo)=>Results.Ok(await DbAnalytics.AbcAsync(db,c.Tenant(),metric??"profit",dateFrom,dateTo)));
api.MapGet("/orders", async (HttpContext c, SellerFinanceDbContext db,string? status,DateOnly? dateFrom,DateOnly? dateTo,string? productId,decimal? profitFrom,decimal? profitTo,string? search,int page=1,int pageSize=50) =>
{
    if(dateFrom.HasValue&&dateTo.HasValue&&dateFrom>dateTo)return Results.BadRequest(new{title="dateFrom не может быть позже dateTo"});
    if(page<1||pageSize is <1 or >100)return Results.BadRequest(new{title="page должен быть не меньше 1, pageSize — от 1 до 100"});
    return Results.Ok(await DbAnalytics.OrdersAsync(db,c.Tenant(),status,dateFrom,dateTo,productId,profitFrom,profitTo,search,page,pageSize));
});
api.MapGet("/orders/{id}",async(HttpContext c,string id,SellerFinanceDbContext db)=>{var result=await DbAnalytics.OrderDetailAsync(db,c.Tenant(),id);return result is null?Results.NotFound():Results.Ok(result);});
api.MapGet("/products",async(HttpContext c,SellerFinanceDbContext db,DateOnly? dateFrom,DateOnly? dateTo,string? search,string? filter,int page=1,int pageSize=50)=>
{
    if(dateFrom.HasValue&&dateTo.HasValue&&dateFrom>dateTo)return Results.BadRequest(new{title="dateFrom не может быть позже dateTo"});
    if(page<1||pageSize is <1 or >100)return Results.BadRequest(new{title="page должен быть не меньше 1, pageSize — от 1 до 100"});
    var rows=await DbAnalytics.ProductsAsync(db,c.Tenant(),dateFrom,dateTo);IEnumerable<object> filtered=rows;
    if(!String.IsNullOrWhiteSpace(search))filtered=filtered.Where(x=>{var json=JsonSerializer.Serialize(x);return json.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase);});
    if(!String.IsNullOrWhiteSpace(filter)){var key=filter.Trim().ToLowerInvariant();filtered=filtered.Where(x=>{using var json=JsonDocument.Parse(JsonSerializer.Serialize(x));var root=json.RootElement;var cost=root.GetProperty("cost");var profit=root.GetProperty("profit");return key switch{"missing"=>cost.ValueKind==JsonValueKind.Null,"profitable"=>profit.ValueKind==JsonValueKind.Number&&profit.GetDecimal()>0,"loss"=>profit.ValueKind==JsonValueKind.Number&&profit.GetDecimal()<0,_=>true};});}
    var materialized=filtered.ToArray();var total=materialized.Length;return Results.Ok(new{items=materialized.Skip((page-1)*pageSize).Take(pageSize),page,pageSize,totalCount=total,totalPages=(int)Math.Ceiling(total/(decimal)pageSize)});
});
api.MapGet("/fee-rules",async(HttpContext c,SellerFinanceDbContext db)=>Results.Ok(await db.FeeRules.AsNoTracking().Where(x=>x.OrganizationId==c.Tenant()).OrderByDescending(x=>x.EffectiveFrom).Select(x=>new{x.Id,scope=x.Scope.ToString(),x.ProductId,x.Category,valueType=x.ValueType.ToString(),x.Value,x.EffectiveFrom,x.EffectiveTo}).ToArrayAsync()));
api.MapPost("/fee-rules",async(HttpContext c,FeeRuleRequest request,SellerFinanceDbContext db)=>
{
    if(!c.Membership().CanWrite())return Results.Forbid();if(!Enum.TryParse<FeeRuleScope>(request.Scope,true,out var scope)||!Enum.TryParse<FeeValueType>(request.ValueType,true,out var type))return Results.BadRequest(new{title="Некорректный тип правила"});if(request.Value<0||type==FeeValueType.Percentage&&request.Value>100)return Results.BadRequest(new{title="Некорректное значение комиссии"});if(scope==FeeRuleScope.Product&&!await db.Products.AnyAsync(x=>x.Id==request.ProductId&&x.OrganizationId==c.Tenant()))return Results.NotFound();
    var rule=new FeeRuleEntity{Id=Guid.NewGuid(),OrganizationId=c.Tenant(),Scope=scope,ProductId=scope==FeeRuleScope.Product?request.ProductId:null,Category=scope==FeeRuleScope.Category?request.Category:null,ValueType=type,Value=request.Value,EffectiveFrom=request.EffectiveFrom,EffectiveTo=request.EffectiveTo,CreatedByUserId=c.User.FindFirstValue(ClaimTypes.NameIdentifier)!};db.FeeRules.Add(rule);AuditWriter.Add(db,c,"fee.rule.created","FeeRule",rule.Id.ToString());await db.SaveChangesAsync();return Results.Created($"/api/v1/fee-rules/{rule.Id}",new{rule.Id});
});
api.MapPost("/order-lines/{id:guid}/actual-fee",async(HttpContext c,Guid id,ActualFeeRequest request,SellerFinanceDbContext db)=>
{
    if(!c.Membership().CanWrite())return Results.Forbid();if(request.Amount<0)return Results.BadRequest(new{title="Сумма не может быть отрицательной"});var line=await(from l in db.OrderLines join o in db.Orders on l.OrderId equals o.Id where l.Id==id&&o.OrganizationId==c.Tenant() select l).SingleOrDefaultAsync();if(line is null)return Results.NotFound();var fee=await db.ActualFees.SingleOrDefaultAsync(x=>x.OrganizationId==c.Tenant()&&x.OrderLineId==id);if(fee is null){fee=new(){Id=Guid.NewGuid(),OrganizationId=c.Tenant(),OrderLineId=id,CreatedByUserId=c.User.FindFirstValue(ClaimTypes.NameIdentifier)!};db.ActualFees.Add(fee);}fee.Amount=request.Amount;fee.Source=request.Source??"Manual";AuditWriter.Add(db,c,"actual.fee.changed","OrderLine",id.ToString());await db.SaveChangesAsync();return Results.Ok(new{fee.Id,fee.Amount});
});
api.MapGet("/expenses",async(HttpContext c,SellerFinanceDbContext db,DateOnly? dateFrom,DateOnly? dateTo)=>Results.Ok(await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==c.Tenant()&&(!dateFrom.HasValue||x.Date>=dateFrom)&&(!dateTo.HasValue||x.Date<=dateTo)).OrderByDescending(x=>x.Date).Select(x=>new{x.Id,type=x.Type.ToString(),x.Amount,x.Date,x.ProductId,x.OrderId,x.Comment,source=x.Source.ToString()}).ToArrayAsync()));
api.MapPost("/expenses",async(HttpContext c,ExpenseRequest request,SellerFinanceDbContext db)=>
{
    if(!c.Membership().CanWrite())return Results.Forbid();if(!Enum.TryParse<ExpenseType>(request.Type,true,out var type)||request.Amount<=0)return Results.BadRequest(new{title="Проверьте тип и сумму расхода"});if(request.ProductId is not null&&!await db.Products.AnyAsync(x=>x.Id==request.ProductId&&x.OrganizationId==c.Tenant()))return Results.NotFound();if(request.OrderId is not null&&!await db.Orders.AnyAsync(x=>x.Id==request.OrderId&&x.OrganizationId==c.Tenant()))return Results.NotFound();var expense=new ExpenseEntity{Id=Guid.NewGuid(),OrganizationId=c.Tenant(),Type=type,Amount=request.Amount,Date=request.Date,ProductId=request.ProductId,OrderId=request.OrderId,Comment=request.Comment?.Trim(),CreatedByUserId=c.User.FindFirstValue(ClaimTypes.NameIdentifier)!};db.Expenses.Add(expense);AuditWriter.Add(db,c,"expense.created","Expense",expense.Id.ToString());await db.SaveChangesAsync();return Results.Created($"/api/v1/expenses/{expense.Id}",new{expense.Id});
});
api.MapDelete("/expenses/{id:guid}",async(HttpContext c,Guid id,SellerFinanceDbContext db)=>{if(!c.Membership().CanWrite())return Results.Forbid();var expense=await db.Expenses.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==c.Tenant());if(expense is null)return Results.NotFound();db.Expenses.Remove(expense);AuditWriter.Add(db,c,"expense.deleted","Expense",id.ToString());await db.SaveChangesAsync();return Results.NoContent();});
api.MapPost("/exports",async(HttpContext c,ExportRequest request,SellerFinanceDbContext db)=>
{
    if(!c.Membership().CanWrite())return Results.Forbid();if(!await FeatureFlags.IsEnabledAsync(db,c.Tenant(),"AdvancedExports"))return Results.Problem("Экспорт отключён администратором SaaS",statusCode:403);var report=request.ReportType.Trim();if(report.ToLowerInvariant() is not("products" or "orders" or "missingcosts" or "abc")||request.Format.ToLowerInvariant() is not("csv" or "xlsx"))return Results.BadRequest(new{title="Неизвестный отчёт или формат"});var token=TokenTools.CreateToken();var job=new ExportJobEntity{Id=Guid.NewGuid(),OrganizationId=c.Tenant(),CreatedByUserId=c.User.FindFirstValue(ClaimTypes.NameIdentifier)!,ReportType=report,Format=request.Format.ToLowerInvariant(),DateFrom=request.DateFrom,DateTo=request.DateTo,DownloadTokenHash=TokenTools.Hash(token)};db.ExportJobs.Add(job);AuditWriter.Add(db,c,"export.queued","ExportJob",job.Id.ToString(),$"{{\"report\":\"{report}\",\"format\":\"{job.Format}\"}}");await db.SaveChangesAsync();return Results.Accepted($"/api/v1/exports/{job.Id}",new{job.Id,status=job.Status.ToString(),downloadToken=token,job.ExpiresAt});
}).RequireRateLimiting("sensitive");
api.MapGet("/exports/{id:guid}",async(HttpContext c,Guid id,SellerFinanceDbContext db)=>{var job=await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==c.Tenant());return job is null?Results.NotFound():Results.Ok(new{job.Id,status=job.Status.ToString(),job.RowCount,job.FileName,job.ExpiresAt,job.ErrorCode});});
api.MapGet("/telegram",async(HttpContext c,SellerFinanceDbContext db,TelegramClient telegram)=>{var connection=await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==c.Tenant());var rules=await db.NotificationRules.AsNoTracking().Where(x=>x.OrganizationId==c.Tenant()).Select(x=>new{x.Id,eventType=x.EventType.ToString(),x.Enabled,x.Threshold}).ToArrayAsync();var deliveries=await db.NotificationDeliveries.AsNoTracking().Where(x=>x.OrganizationId==c.Tenant()).OrderByDescending(x=>x.CreatedAt).Take(10).Select(x=>new{x.Id,eventType=x.EventType.ToString(),status=x.Status.ToString(),x.CreatedAt,x.SentAt,x.ErrorCode}).ToArrayAsync();return Results.Ok(new{configured=telegram.IsConfigured,status=connection?.Status??"NotLinked",connection?.LinkedAt,rules,deliveries});});
api.MapPost("/telegram/link",async(HttpContext c,SellerFinanceDbContext db,IConfiguration config,TelegramClient telegram)=>
{
    if(!c.Membership().CanManageMembers())return Results.Forbid();if(!telegram.IsConfigured)return Results.Problem("Telegram bot не настроен администратором SaaS",statusCode:503);var code=TokenTools.CreateToken()[..12];var connection=await db.TelegramConnections.SingleOrDefaultAsync(x=>x.OrganizationId==c.Tenant());if(connection is null){connection=new(){Id=Guid.NewGuid(),OrganizationId=c.Tenant()};db.TelegramConnections.Add(connection);}connection.LinkCodeHash=TokenTools.Hash(code);connection.LinkCodeExpiresAt=DateTimeOffset.UtcNow.AddMinutes(15);connection.Status="Pending";connection.ChatId=null;AuditWriter.Add(db,c,"telegram.link.started","TelegramConnection",connection.Id.ToString());await db.SaveChangesAsync();var username=config["TELEGRAM_BOT_USERNAME"];return Results.Ok(new{code,expiresInMinutes=15,deepLink=String.IsNullOrWhiteSpace(username)?null:$"https://t.me/{username}?start={code}"});
}).RequireRateLimiting("sensitive");
api.MapPost("/telegram/test",async(HttpContext c,SellerFinanceDbContext db,TelegramClient telegram,CancellationToken ct)=>{var connection=await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==c.Tenant()&&x.Status=="Active"&&x.ChatId!=null,ct);if(connection is null)return Results.Conflict(new{title="Telegram не связан"});return await telegram.SendAsync(connection.ChatId!.Value,"Тест Seller Finance: уведомления работают.",ct)?Results.Ok(new{sent=true}):Results.Problem("Telegram API недоступен",statusCode:503);}).RequireRateLimiting("sensitive");
api.MapPut("/telegram/rules/{eventType}",async(HttpContext c,string eventType,NotificationRuleRequest request,SellerFinanceDbContext db)=>{if(!c.Membership().CanWrite())return Results.Forbid();if(!Enum.TryParse<NotificationEventType>(eventType,true,out var type))return Results.BadRequest(new{title="Неизвестное событие"});var rule=await db.NotificationRules.SingleOrDefaultAsync(x=>x.OrganizationId==c.Tenant()&&x.EventType==type);if(rule is null){rule=new(){Id=Guid.NewGuid(),OrganizationId=c.Tenant(),EventType=type};db.NotificationRules.Add(rule);}rule.Enabled=request.Enabled;rule.Threshold=request.Threshold;AuditWriter.Add(db,c,"notification.rule.changed","NotificationRule",rule.Id.ToString(),$"{{\"eventType\":\"{type}\",\"enabled\":{request.Enabled.ToString().ToLowerInvariant()}}}");await db.SaveChangesAsync();return Results.Ok(new{rule.Id});});
api.MapGet("/admin/organizations",async(HttpContext c,SellerFinanceDbContext db,IConfiguration config)=>SaasSecurity.IsAdmin(c.User,config)?Results.Ok(await(from organization in db.Organizations.AsNoTracking() join subscription in db.Subscriptions.AsNoTracking() on organization.Id equals subscription.OrganizationId orderby organization.CreatedAt descending select new{organization.Id,organization.Name,plan=subscription.Plan.ToString(),subscriptionStatus=subscription.Status.ToString(),organization.Status,organization.CreatedAt,subscription.TrialEndsAt,subscription.BillingPeriod,subscription.PeriodStart,subscription.PeriodEnd,memberCount=db.OrganizationUsers.Count(m=>m.OrganizationId==organization.Id&&m.JoinedAt!=null),storeCount=db.MarketplaceConnections.Count(m=>m.OrganizationId==organization.Id&&m.Provider=="Kaspi"&&m.Status!=MarketplaceConnectionStatus.Disabled),lastActivity=db.AuditLogs.Where(a=>a.OrganizationId==organization.Id).Max(a=>(DateTimeOffset?)a.CreatedAt),lastSync=db.MarketplaceConnections.Where(m=>m.OrganizationId==organization.Id).Max(m=>(DateTimeOffset?)m.LastSuccessfulSyncAt),lastSyncStatus=db.SyncJobs.Where(j=>j.OrganizationId==organization.Id).OrderByDescending(j=>j.CreatedAt).Select(j=>j.Status.ToString()).FirstOrDefault()}).ToArrayAsync()):Results.Forbid());
api.MapPut("/admin/organizations/{id}/plan",async(HttpContext c,string id,AdminPlanRequest request,SellerFinanceDbContext db,IConfiguration config)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();if(!Enum.TryParse<SubscriptionPlan>(request.Plan,true,out var plan))return Results.BadRequest(new{title="Неизвестный тариф"});var billingPeriod=request.BillingPeriod??"Monthly";if(billingPeriod is not("Monthly" or "Annual"))return Results.BadRequest(new{title="BillingPeriod должен быть Monthly или Annual"});var subscription=await db.Subscriptions.SingleOrDefaultAsync(x=>x.OrganizationId==id);if(subscription is null)return Results.NotFound();var stores=await db.MarketplaceConnections.CountAsync(x=>x.OrganizationId==id&&x.Status!=MarketplaceConnectionStatus.Disabled);var members=await db.OrganizationUsers.CountAsync(x=>x.OrganizationId==id&&x.JoinedAt!=null);if(stores>PlanLimits.MaxStores(plan)||members>PlanLimits.MaxMembers(plan))return Results.Conflict(new{title="Сначала уменьшите число магазинов или пользователей до лимита нового тарифа"});var now=DateTimeOffset.UtcNow;subscription.Plan=plan;subscription.Status=plan==SubscriptionPlan.Trial?SubscriptionStatus.Trialing:SubscriptionStatus.Active;subscription.BillingPeriod=billingPeriod;subscription.PeriodStart=now;subscription.PeriodEnd=plan==SubscriptionPlan.Trial?now.AddDays(14):billingPeriod=="Annual"?now.AddYears(1):now.AddMonths(1);subscription.TrialEndsAt=plan==SubscriptionPlan.Trial?subscription.PeriodEnd:null;subscription.UpdatedAt=now;AuditWriter.AddSystem(db,c,id,"saas.plan.changed","Subscription",subscription.Id.ToString(),JsonSerializer.Serialize(new{plan=plan.ToString(),subscription.BillingPeriod}));await db.SaveChangesAsync();return Results.NoContent();});
api.MapPut("/admin/organizations/{id}/status",async(HttpContext c,string id,AdminStatusRequest request,SellerFinanceDbContext db,IConfiguration config)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();var status=request.Status.Trim();if(status is not("Active" or "Suspended"))return Results.BadRequest(new{title="Допустимы статусы Active и Suspended"});if(status=="Suspended"&&id==c.Tenant())return Results.Conflict(new{title="Нельзя заблокировать текущую организацию SaaS Admin"});if(status=="Suspended"&&String.IsNullOrWhiteSpace(request.Reason))return Results.BadRequest(new{title="Укажите безопасную причину блокировки"});var organization=await db.Organizations.SingleOrDefaultAsync(x=>x.Id==id);if(organization is null)return Results.NotFound();organization.Status=status;AuditWriter.AddSystem(db,c,id,status=="Active"?"saas.organization.activated":"saas.organization.suspended","Organization",id,JsonSerializer.Serialize(new{reason=request.Reason?.Trim()}));await db.SaveChangesAsync();return Results.NoContent();}).RequireRateLimiting("sensitive");
api.MapGet("/admin/sync-jobs",async(HttpContext c,SellerFinanceDbContext db,IConfiguration config,string? organizationId,string? status,int page=1,int pageSize=50)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();if(page<1||pageSize is <1 or >100)return Results.BadRequest(new{title="Некорректная пагинация"});var query=db.SyncJobs.AsNoTracking().AsQueryable();if(!String.IsNullOrWhiteSpace(organizationId))query=query.Where(x=>x.OrganizationId==organizationId);if(!String.IsNullOrWhiteSpace(status)&&Enum.TryParse<SyncJobStatus>(status,true,out var parsed))query=query.Where(x=>x.Status==parsed);var total=await query.CountAsync();var items=await query.OrderByDescending(x=>x.CreatedAt).Skip((page-1)*pageSize).Take(pageSize).Select(x=>new{x.Id,x.OrganizationId,x.MarketplaceConnectionId,status=x.Status.ToString(),x.Attempt,x.ImportedOrders,x.ErrorCode,x.WindowFrom,x.WindowTo,x.CreatedAt,x.StartedAt,x.CompletedAt}).ToArrayAsync();return Results.Ok(new{items,page,pageSize,totalCount=total,totalPages=(int)Math.Ceiling(total/(decimal)pageSize)});});
api.MapPost("/admin/sync-jobs/{id:guid}/retry",async(HttpContext c,Guid id,SellerFinanceDbContext db,IConfiguration config)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();var result=await SaasAdminOperations.RetrySyncAsync(db,id);if(result.Failure==SyncRetryFailure.NotFound)return Results.NotFound();if(result.Failure!=SyncRetryFailure.None)return Results.Conflict(new{title=result.Failure switch{SyncRetryFailure.NotRetryable=>"Повтор разрешён только для задания RequiresAttention",SyncRetryFailure.OrganizationDisabled=>"Сначала активируйте организацию и KaspiSync",_=>"Для подключения уже выполняется задание"}});var retry=result.Job!;AuditWriter.AddSystem(db,c,retry.OrganizationId,"saas.sync.retry.queued","SyncJob",retry.Id.ToString(),JsonSerializer.Serialize(new{sourceJobId=id}));await db.SaveChangesAsync();return Results.Accepted($"/api/v1/admin/sync-jobs/{retry.Id}",new{retry.Id,status=retry.Status.ToString()});}).RequireRateLimiting("sensitive");
api.MapGet("/admin/organizations/{id}/feature-flags",async(HttpContext c,string id,SellerFinanceDbContext db,IConfiguration config)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();if(!await db.Organizations.AnyAsync(x=>x.Id==id))return Results.NotFound();var stored=await db.OrganizationFeatureFlags.AsNoTracking().Where(x=>x.OrganizationId==id).ToDictionaryAsync(x=>x.Key,x=>x.Enabled);return Results.Ok(FeatureFlags.Known.Select(key=>new{key,enabled=stored.GetValueOrDefault(key,true)}));});
api.MapPut("/admin/organizations/{id}/feature-flags/{key}",async(HttpContext c,string id,string key,FeatureFlagRequest request,SellerFinanceDbContext db,IConfiguration config)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();var canonical=FeatureFlags.Known.SingleOrDefault(x=>String.Equals(x,key,StringComparison.OrdinalIgnoreCase));if(canonical is null)return Results.BadRequest(new{title="Неизвестный feature flag"});if(!await db.Organizations.AnyAsync(x=>x.Id==id))return Results.NotFound();var flag=await db.OrganizationFeatureFlags.SingleOrDefaultAsync(x=>x.OrganizationId==id&&x.Key==canonical);if(flag is null){flag=new(){Id=Guid.NewGuid(),OrganizationId=id,Key=canonical};db.OrganizationFeatureFlags.Add(flag);}flag.Enabled=request.Enabled;flag.UpdatedAt=DateTimeOffset.UtcNow;flag.UpdatedByUserId=c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;AuditWriter.AddSystem(db,c,id,"saas.feature-flag.changed","OrganizationFeatureFlag",flag.Id.ToString(),JsonSerializer.Serialize(new{key=canonical,request.Enabled}));await db.SaveChangesAsync();return Results.NoContent();});
api.MapGet("/admin/audit",async(HttpContext c,SellerFinanceDbContext db,IConfiguration config,string? organizationId,int take=100)=>{if(!SaasSecurity.IsAdmin(c.User,config))return Results.Forbid();take=Math.Clamp(take,1,200);var query=db.AuditLogs.AsNoTracking().Where(x=>x.Action.StartsWith("saas."));if(!String.IsNullOrWhiteSpace(organizationId))query=query.Where(x=>x.OrganizationId==organizationId);return Results.Ok(await query.OrderByDescending(x=>x.CreatedAt).Take(take).Select(x=>new{x.Id,x.OrganizationId,x.UserId,x.Action,x.EntityType,x.EntityId,x.CreatedAt,x.MetadataSafe}).ToArrayAsync());});
api.MapGet("/kaspi/connections",async(HttpContext ctx,SellerFinanceDbContext db)=>
{
    var subscription=await Subscriptions.GetAsync(db,ctx.Tenant());var rows=await db.MarketplaceConnections.AsNoTracking().Where(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi").OrderBy(x=>x.CreatedAt).Select(x=>new{x.Id,x.DisplayName,status=x.Status.ToString(),x.LastVerifiedAt,x.LastSuccessfulSyncAt,x.LastErrorCode,lastJob=db.SyncJobs.Where(j=>j.MarketplaceConnectionId==x.Id).OrderByDescending(j=>j.CreatedAt).Select(j=>new{j.Id,status=j.Status.ToString(),j.ImportedOrders,j.ErrorCode,j.CreatedAt}).FirstOrDefault()}).ToArrayAsync();return Results.Ok(new{items=rows,maxStores=PlanLimits.MaxStores(subscription.Plan),activeStores=rows.Count(x=>x.status!="Disabled")});
});
api.MapPost("/kaspi/connections",async(HttpContext ctx,KaspiConnectionRequest request,SellerFinanceDbContext db,TokenCipher cipher,KaspiClient kaspi,CancellationToken ct)=>
{
    if(!ctx.Membership().CanManageMembers())return Results.Forbid();var displayName=request.DisplayName?.Trim()??"";if(displayName.Length is <2 or >80||String.IsNullOrWhiteSpace(request.Token))return Results.BadRequest(new{title="Укажите название магазина и API-токен Kaspi"});var subscription=await Subscriptions.GetAsync(db,ctx.Tenant(),ct);var existing=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi"&&x.DisplayName==displayName,ct);if(existing is not null&&existing.Status!=MarketplaceConnectionStatus.Disabled)return Results.Conflict(new{title="Магазин с таким названием уже подключён"});var count=await db.MarketplaceConnections.CountAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi"&&x.Status!=MarketplaceConnectionStatus.Disabled,ct);if(count>=PlanLimits.MaxStores(subscription.Plan))return Results.Problem("Достигнут лимит магазинов тарифа",statusCode:402);KaspiResult verification;try{verification=await kaspi.GetOrdersAsync(request.Token.Trim(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,ct);}catch(HttpRequestException){return Results.Problem("Kaspi API временно недоступен",statusCode:503);}if(!verification.Success)return Results.Problem(verification.ErrorCode,statusCode:(int)verification.StatusCode);await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);count=await db.MarketplaceConnections.CountAsync(x=>x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi"&&x.Status!=MarketplaceConnectionStatus.Disabled,ct);if(count>=PlanLimits.MaxStores(subscription.Plan)){await transaction.RollbackAsync(ct);return Results.Problem("Достигнут лимит магазинов тарифа",statusCode:402);}var encrypted=cipher.Encrypt(request.Token.Trim());var connection=existing??new(){Id=Guid.NewGuid(),OrganizationId=ctx.Tenant(),DisplayName=displayName};if(existing is null)db.MarketplaceConnections.Add(connection);connection.TokenCiphertext=encrypted.Ciphertext;connection.TokenNonce=encrypted.Nonce;connection.TokenTag=encrypted.Tag;connection.Status=MarketplaceConnectionStatus.Active;connection.LastVerifiedAt=DateTimeOffset.UtcNow;connection.LastErrorCode=null;connection.UpdatedAt=DateTimeOffset.UtcNow;AuditWriter.Add(db,ctx,"integration.connected","MarketplaceConnection",connection.Id.ToString(),JsonSerializer.Serialize(new{provider="Kaspi",displayName}));await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);return Results.Created($"/api/v1/kaspi/connections/{connection.Id}",new{connection.Id,connection.DisplayName,status=connection.Status.ToString()});
}).RequireRateLimiting("sensitive");
api.MapPut("/kaspi/connections/{id:guid}/token",async(HttpContext ctx,Guid id,KaspiConnectionRequest request,SellerFinanceDbContext db,TokenCipher cipher,KaspiClient kaspi,CancellationToken ct)=>
{
    if(!ctx.Membership().CanManageMembers())return Results.Forbid();var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi",ct);if(connection is null)return Results.NotFound();if(String.IsNullOrWhiteSpace(request.Token))return Results.BadRequest(new{title="Укажите API-токен Kaspi"});var result=await kaspi.GetOrdersAsync(request.Token.Trim(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,ct);if(!result.Success)return Results.Problem(result.ErrorCode,statusCode:(int)result.StatusCode);var encrypted=cipher.Encrypt(request.Token.Trim());connection.TokenCiphertext=encrypted.Ciphertext;connection.TokenNonce=encrypted.Nonce;connection.TokenTag=encrypted.Tag;connection.Status=MarketplaceConnectionStatus.Active;connection.LastVerifiedAt=DateTimeOffset.UtcNow;connection.LastErrorCode=null;connection.UpdatedAt=DateTimeOffset.UtcNow;AuditWriter.Add(db,ctx,"integration.token.replaced","MarketplaceConnection",id.ToString());await db.SaveChangesAsync(ct);return Results.NoContent();
}).RequireRateLimiting("sensitive");
api.MapPost("/kaspi/connections/{id:guid}/verify",async(HttpContext ctx,Guid id,SellerFinanceDbContext db,TokenCipher cipher,KaspiClient kaspi,CancellationToken ct)=>
{
    if(!ctx.Membership().CanManageMembers())return Results.Forbid();var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi"&&x.Status!=MarketplaceConnectionStatus.Disabled,ct);if(connection is null)return Results.NotFound();var result=await kaspi.GetOrdersAsync(cipher.Decrypt(connection),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,ct);connection.Status=result.Success?MarketplaceConnectionStatus.Active:MarketplaceConnectionStatus.RequiresAttention;connection.LastVerifiedAt=result.Success?DateTimeOffset.UtcNow:null;connection.LastErrorCode=result.ErrorCode;await db.SaveChangesAsync(ct);return result.Success?Results.Ok(new{status="Active"}):Results.Problem(result.ErrorCode,statusCode:(int)result.StatusCode);
}).RequireRateLimiting("sensitive");
api.MapPost("/kaspi/connections/{id:guid}/sync",async(HttpContext ctx,Guid id,SellerFinanceDbContext db,CancellationToken ct)=>
{
    if(!ctx.Membership().CanWrite())return Results.Forbid();if(!await FeatureFlags.IsEnabledAsync(db,ctx.Tenant(),"KaspiSync",ct))return Results.Problem("Синхронизация отключена администратором SaaS",statusCode:403);var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi"&&x.Status!=MarketplaceConnectionStatus.Disabled,ct);if(connection is null)return Results.NotFound();if(await db.SyncJobs.AnyAsync(x=>x.MarketplaceConnectionId==connection.Id&&(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.Running||x.Status==SyncJobStatus.RetryScheduled),ct))return Results.Conflict(new{title="Синхронизация уже выполняется"});var subscription=await Subscriptions.GetAsync(db,ctx.Tenant(),ct);var now=DateTimeOffset.UtcNow;var job=new SyncJobEntity{Id=Guid.NewGuid(),OrganizationId=ctx.Tenant(),MarketplaceConnectionId=connection.Id,WindowFrom=connection.LastSuccessfulSyncAt.HasValue?now.AddDays(-14):PlanLimits.InitialHistoryFrom(subscription.Plan,now),WindowTo=now};db.SyncJobs.Add(job);AuditWriter.Add(db,ctx,"integration.sync.queued","SyncJob",job.Id.ToString(),JsonSerializer.Serialize(new{connectionId=id}));await db.SaveChangesAsync(ct);return Results.Accepted($"/api/v1/kaspi/sync/{job.Id}",new{job.Id,status=job.Status.ToString()});
});
api.MapDelete("/kaspi/connections/{id:guid}",async(HttpContext ctx,Guid id,SellerFinanceDbContext db,CancellationToken ct)=>
{
    if(!ctx.Membership().CanManageMembers())return Results.Forbid();var connection=await db.MarketplaceConnections.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==ctx.Tenant()&&x.Provider=="Kaspi",ct);if(connection is null)return Results.NotFound();if(await db.SyncJobs.AnyAsync(x=>x.MarketplaceConnectionId==id&&(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.Running||x.Status==SyncJobStatus.RetryScheduled),ct))return Results.Conflict(new{title="Дождитесь завершения синхронизации"});connection.Status=MarketplaceConnectionStatus.Disabled;connection.TokenCiphertext=[];connection.TokenNonce=[];connection.TokenTag=[];connection.UpdatedAt=DateTimeOffset.UtcNow;AuditWriter.Add(db,ctx,"integration.disconnected","MarketplaceConnection",id.ToString());await db.SaveChangesAsync(ct);return Results.NoContent();
}).RequireRateLimiting("sensitive");
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
record OrganizationSettingsRequest(string Name,string TimeZone,string Currency);
record AcceptInvitationRequest(string Token);
record ProductCostRequest(decimal Cost,DateOnly? EffectiveFrom);
record KaspiConnectionRequest(string Token,string? DisplayName=null);
record FeeRuleRequest(string Scope,string ValueType,decimal Value,DateOnly EffectiveFrom,DateOnly? EffectiveTo,string? ProductId,string? Category);
record ActualFeeRequest(decimal Amount,string? Source);
record ExpenseRequest(string Type,decimal Amount,DateOnly Date,string? ProductId,string? OrderId,string? Comment);
record ExportRequest(string ReportType,string Format,DateOnly? DateFrom,DateOnly? DateTo);
record NotificationRuleRequest(bool Enabled,decimal? Threshold);
record AdminPlanRequest(string Plan,string? BillingPeriod=null);
record AdminStatusRequest(string Status,string? Reason);
record FeatureFlagRequest(bool Enabled);
record ChangeRoleRequest(string Role);
public partial class Program { }
