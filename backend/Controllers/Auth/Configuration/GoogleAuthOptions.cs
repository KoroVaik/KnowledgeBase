namespace Backend.Controllers.Auth.Configuration;

public sealed class GoogleAuthOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    // Empty means nobody gets in: a missing allow-list must not read as "anyone with a Google
    // account", because the sign-in itself succeeds for every account on earth.
    public IReadOnlyList<string> AllowedEmails { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public bool Allows(string? email) =>
        email is not null && AllowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
}
