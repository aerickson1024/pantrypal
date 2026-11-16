using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using PantryPal.Api.Services;

namespace PantryPal.Api.Endpoints;

public static class AccountEndpoints
{
    public const string RateLimitPolicy = "account-auth";

    private const int MaxEmailLength = 255;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;
    private const int MaxNameLength = 100;
    private const int MaxInviteCodeLength = 100;
    private const int MaxGeminiApiKeyLength = 255;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/account").WithTags("Account");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("Register")
            .WithSummary("Create an account with an invite code and sign in")
            .Produces<AccountProfile>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("Login")
            .WithSummary("Sign in with email and password")
            .Produces<AccountProfile>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Sign out and clear the session cookie");

        group.MapGet("/me", GetProfileAsync)
            .WithName("GetProfile")
            .WithSummary("Get the signed-in account")
            .Produces<AccountProfile>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/me", UpdateProfileAsync)
            .WithName("UpdateProfile")
            .WithSummary("Update the signed-in account names")
            .Produces<AccountProfile>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/gemini-key", GetGeminiKeyAsync)
            .WithName("GetGeminiKey")
            .WithSummary("Get the saved Gemini API key for the signed-in account")
            .Produces<GeminiKeyResponse>(StatusCodes.Status200OK);

        group.MapPut("/gemini-key", SetGeminiKeyAsync)
            .WithName("SetGeminiKey")
            .WithSummary("Save the Gemini API key for the signed-in account")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/gemini-key", ClearGeminiKeyAsync)
            .WithName("ClearGeminiKey")
            .WithSummary("Remove the saved Gemini API key")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    public static ulong? GetUserId(this ClaimsPrincipal principal) =>
        ulong.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var userId)
            ? userId
            : null;

    private static async Task<IResult> RegisterAsync(
        RegisterRequest? request,
        HttpContext httpContext,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request?.Email);
        var password = request?.Password ?? string.Empty;
        var firstName = request?.FirstName?.Trim() ?? string.Empty;
        var lastName = request?.LastName?.Trim() ?? string.Empty;
        var inviteCode = request?.InviteCode?.Trim() ?? string.Empty;

        if (!IsValidEmail(email))
        {
            return InvalidRequest("Enter a valid email address.");
        }

        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return InvalidRequest($"Password must be {MinPasswordLength} to {MaxPasswordLength} characters.");
        }

        if (firstName.Length is 0 or > MaxNameLength)
        {
            return InvalidRequest($"First name must be 1 to {MaxNameLength} characters.");
        }

        if (lastName.Length is 0 or > MaxNameLength)
        {
            return InvalidRequest($"Last name must be 1 to {MaxNameLength} characters.");
        }

        if (inviteCode.Length is 0 or > MaxInviteCodeLength)
        {
            return InvalidRequest("An invite code is required.");
        }

        var result = await accountService.RegisterAsync(email, password, firstName, lastName, inviteCode, cancellationToken);
        switch (result.Outcome)
        {
            case RegisterOutcome.EmailTaken:
                return InvalidRequest("Could not create the account. Check your details, or sign in if you already have one.");
            case RegisterOutcome.InvalidInviteCode:
                return InvalidRequest("The invite code is not valid or has already been used.");
        }

        await SignInAsync(httpContext, result.UserId!.Value);
        var profile = await accountService.GetProfileAsync(result.UserId.Value, cancellationToken);
        return TypedResults.Created("/api/account/me", profile);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        HttpContext httpContext,
        IAccountService accountService,
        LoginThrottle loginThrottle,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request?.Email);
        var password = request?.Password ?? string.Empty;
        if (email.Length == 0 || password.Length == 0)
        {
            return InvalidRequest("Enter your email and password.");
        }

        if (loginThrottle.IsLocked(email))
        {
            return TypedResults.Problem(
                title: "Too many sign-in attempts.",
                detail: "Wait 15 minutes, then try again.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var userId = await accountService.VerifyCredentialsAsync(email, password, cancellationToken);
        if (userId is null)
        {
            loginThrottle.RecordFailure(email);
            return TypedResults.Problem(
                title: "Sign-in failed.",
                detail: "The email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        loginThrottle.Reset(email);
        await SignInAsync(httpContext, userId.Value);
        var profile = await accountService.GetProfileAsync(userId.Value, cancellationToken);
        return TypedResults.Ok(profile);
    }

    private static Task LogoutAsync(HttpContext httpContext) =>
        httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal user,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var profile = await accountService.GetProfileAsync(userId, cancellationToken);
        return profile is null ? TypedResults.Unauthorized() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest? request,
        ClaimsPrincipal user,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var firstName = request?.FirstName?.Trim() ?? string.Empty;
        var lastName = request?.LastName?.Trim() ?? string.Empty;
        if (firstName.Length is 0 or > MaxNameLength)
        {
            return InvalidRequest($"First name must be 1 to {MaxNameLength} characters.");
        }

        if (lastName.Length is 0 or > MaxNameLength)
        {
            return InvalidRequest($"Last name must be 1 to {MaxNameLength} characters.");
        }

        if (!await accountService.UpdateNameAsync(userId, firstName, lastName, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        var profile = await accountService.GetProfileAsync(userId, cancellationToken);
        return profile is null ? TypedResults.Unauthorized() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> GetGeminiKeyAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        var apiKey = await accountService.GetGeminiApiKeyAsync(userId, cancellationToken);
        return TypedResults.Ok(new GeminiKeyResponse(apiKey));
    }

    private static async Task<IResult> SetGeminiKeyAsync(
        GeminiKeyRequest? request,
        ClaimsPrincipal user,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var apiKey = request?.ApiKey?.Trim() ?? string.Empty;
        if (apiKey.Length is 0 or > MaxGeminiApiKeyLength || apiKey.Any(char.IsWhiteSpace))
        {
            return InvalidRequest("Enter a valid Gemini API key.");
        }

        return await accountService.SetGeminiApiKeyAsync(userId, apiKey, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.Unauthorized();
    }

    private static async Task<IResult> ClearGeminiKeyAsync(
        ClaimsPrincipal user,
        IAccountService accountService,
        CancellationToken cancellationToken)
    {
        if (user.GetUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        return await accountService.ClearGeminiApiKeyAsync(userId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.Unauthorized();
    }

    private static Task SignInAsync(HttpContext httpContext, ulong userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)) },
            CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
    }

    private static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > MaxEmailLength)
        {
            return false;
        }

        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IResult InvalidRequest(string detail) =>
        TypedResults.Problem(
            title: "Invalid request.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

public sealed record RegisterRequest(string? Email, string? Password, string? FirstName, string? LastName, string? InviteCode);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UpdateProfileRequest(string? FirstName, string? LastName);

public sealed record GeminiKeyRequest(string? ApiKey);

public sealed record GeminiKeyResponse(string? ApiKey);
