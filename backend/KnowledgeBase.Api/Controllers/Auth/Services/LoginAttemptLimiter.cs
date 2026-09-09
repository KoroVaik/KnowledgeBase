using System.Threading.RateLimiting;
using KnowledgeBase.Api.Controllers.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Api.Controllers.Auth.Services;

// Driven by hand rather than by the rate-limiting middleware: the middleware would spend an
// attempt on every request, including the successful sign-in that ends the guessing.
public sealed class LoginAttemptLimiter : ILoginAttemptLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginAttemptLimiter(IOptions<AuthOptions> options)
    {
        var auth = options.Value;

        _limiter = PartitionedRateLimiter.Create<string, string>(client =>
            RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = auth.MaxFailedLoginsPerWindow,
                Window = auth.FailedLoginWindow,
            }));
    }

    // Statistics rather than a zero-permit probe: acquiring 0 permits always succeeds, so it
    // cannot answer whether the window is exhausted.
    public bool IsBlocked(string client) =>
        _limiter.GetStatistics(client) is { CurrentAvailablePermits: < 1 };

    public void RecordFailure(string client) => _limiter.AttemptAcquire(client).Dispose();

    public void Dispose() => _limiter.Dispose();
}
