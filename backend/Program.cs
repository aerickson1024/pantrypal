using PantryPal.Api.Endpoints;
using PantryPal.Api.Data;
using PantryPal.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddScoped<IReceiptItemNameResolver, ReceiptItemNameResolver>();

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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithSummary("Check API health")
    .Produces(StatusCodes.Status200OK);

app.MapGeminiEndpoints();
app.MapFoodItemEndpoints();
app.MapPurchaseEndpoints();
app.MapDatabaseHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
