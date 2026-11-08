using Microsoft.EntityFrameworkCore;
using PantryPal.Api.Data;

namespace PantryPal.Api.Endpoints;

public static class DatabaseHealthEndpoints
{
    public static IEndpointRouteBuilder MapDatabaseHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/database", CheckDatabaseAsync)
            .WithName("DatabaseHealthCheck")
            .WithSummary("Check MySQL connectivity")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> CheckDatabaseAsync(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("PantryPal")))
        {
            return DatabaseUnavailable();
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PantryPalDbContext>();
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ok" })
                : DatabaseUnavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DatabaseUnavailable();
        }
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        title: "Database unavailable.",
        detail: "The database connection could not be established. Check the local MySQL service and backend connection-string configuration.",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}