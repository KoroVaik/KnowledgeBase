namespace Backend.Controllers.Auth.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string OwnerName { get; set; } = "owner";

    // Temporary: plain password in source, for local debugging only. Must move back to a
    // hash in configuration before anything is deployed.
    public string Password { get; set; } = "baba";

    public int MaxFailedLoginsPerWindow { get; set; } = 5;

    public TimeSpan FailedLoginWindow { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public GoogleAuthOptions Google { get; set; } = new();
}
