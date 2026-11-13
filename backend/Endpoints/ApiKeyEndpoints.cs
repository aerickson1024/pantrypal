using System.Data.Common;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public const string HeaderName = "X-Api-Key";
    public const string VerifyRateLimitPolicy = "verify-key";

    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/verify-key", VerifyKeyAsync)
            .WithName("VerifyApiKey")
            .WithSummary("Check whether the X-Api-Key header matches an active key")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(VerifyRateLimitPolicy);

        return endpoints;
    }

    /// <summary>Rejects the request with 401 unless the X-Api-Key header is an active key.</summary>
    public static RouteHandlerBuilder RequireApiKey(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var apiKey = context.HttpContext.Request.Headers[HeaderName].ToString();
            var validator = context.HttpContext.RequestServices.GetRequiredService<IApiKeyValidator>();
            var check = await CheckAsync(validator, apiKey, context.HttpContext, context.HttpContext.RequestAborted);
            return check ?? await next(context);
        });

    private static async Task<IResult> VerifyKeyAsync(
        HttpRequest request,
        IApiKeyValidator validator,
        CancellationToken cancellationToken)
    {
        var apiKey = request.Headers[HeaderName].ToString();
        var result = await CheckAsync(validator, apiKey, request.HttpContext, cancellationToken);
        return result ?? TypedResults.Ok(new { valid = true });
    }

    // Returns null when the key is valid, otherwise the response to send.
    private static async Task<IResult?> CheckAsync(
        IApiKeyValidator validator,
        string apiKey,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await validator.IsValidAsync(apiKey, cancellationToken))
            {
                return null;
            }

            return TypedResults.Problem(
                title: "A valid Gemini API key is required.",
                detail: "Enter a valid API key on the API Key page.",
                statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(ApiKeyEndpoints))
                .LogError(exception, "The API key could not be verified.");
            return TypedResults.Problem(
                title: "API key verification unavailable.",
                detail: "The API key could not be checked. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
