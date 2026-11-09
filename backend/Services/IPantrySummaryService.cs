using PantryPal.Api.Endpoints;

namespace PantryPal.Api.Services;

public interface IPantrySummaryService
{
    Task<PantrySummaryResponse> GetSummaryAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken);
}