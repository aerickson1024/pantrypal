using Microsoft.Extensions.Caching.Memory;

namespace PantryPal.Api.Services;

// Counts failures per email, whether or not the account exists, so the lockout does not reveal accounts.
public sealed class LoginThrottle(IMemoryCache cache)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public bool IsLocked(string email) =>
        cache.TryGetValue(Key(email), out int failures) && failures >= MaxFailures;

    public void RecordFailure(string email)
    {
        var key = Key(email);
        var failures = cache.TryGetValue(key, out int current) ? current + 1 : 1;
        cache.Set(key, failures, LockoutWindow);
    }

    public void Reset(string email) => cache.Remove(Key(email));

    private static string Key(string email) => "login-failures:" + email;
}
