using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using PantryPal.Api.Endpoints;
using PantryPal.Api.Data;
using PantryPal.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiKeyEndpoints.VerifyRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddScoped<IApiKeyValidator, ApiKeyValidator>();
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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithSummary("Check API health")
    .Produces(StatusCodes.Status200OK);

app.MapGeminiEndpoints();
app.MapFoodItemEndpoints();
app.MapFoodTagEndpoints();
app.MapPurchaseEndpoints();
app.MapDatabaseHealthEndpoints();
app.MapApiKeyEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapFallbackToFile("index.html");

app.Run();
