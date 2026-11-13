namespace PantryPal.Api.Services;

public interface IApiKeyValidator
{
    Task<bool> IsValidAsync(string? apiKey, CancellationToken cancellationToken);
}
