using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using PantryPal.Api.Endpoints;
using PantryPal.Api.Data;
using PantryPal.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(1));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccountEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeysPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "keys")));

// Cookie sessions. Unauthenticated API calls get 401 and forbidden calls get 403, never redirects.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PantryPal.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// Every endpoint requires a signed-in user unless it opts out with AllowAnonymous().
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddScoped<IReceiptItemNameResolver, ReceiptItemNameResolver>();
builder.Services.AddScoped<FoodItemSearchService>();
builder.Services.AddScoped<IPantrySummaryService, PantrySummaryService>();
builder.Services.AddScoped<IFoodTagService, FoodTagService>();

var connectionString = builder.Configuration.GetConnectionString("PantryPal");
builder.Services.AddDbContext<PantryPalDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseMySQL(connectionString);
    }
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (args is ["create-invite", ..])
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("ConnectionStrings:PantryPal is not configured.");
        return 1;
    }

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PantryPalDbContext>();
    var code = Guid.NewGuid();
    dbContext.InviteCodes.Add(new PantryPal.Api.Data.InviteCode
    {
        CodeHash = InviteCodes.Hash(code),
        CreatedAt = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync();

    // Shown once; only the hash is stored.
    Console.WriteLine(code.ToString("D"));
    return 0;
}

app.UseForwardedHeaders();
app.UseHsts();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Static files come before authorization so the public bundle is not gated by the fallback policy.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithName("HealthCheck")
    .WithSummary("Check API health")
    .Produces(StatusCodes.Status200OK);

app.MapGeminiEndpoints();
app.MapFoodItemEndpoints();
app.MapFoodTagEndpoints();
app.MapPurchaseEndpoints();
app.MapDatabaseHealthEndpoints();
app.MapAccountEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
return 0;
