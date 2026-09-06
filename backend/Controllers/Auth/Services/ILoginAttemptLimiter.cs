namespace Backend.Controllers.Auth.Services;

public interface ILoginAttemptLimiter
{
    bool IsBlocked(string client);

    void RecordFailure(string client);
}
