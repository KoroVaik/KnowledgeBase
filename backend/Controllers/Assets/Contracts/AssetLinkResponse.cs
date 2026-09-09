namespace Backend.Controllers.Assets.Contracts;

public sealed record AssetLinkResponse(string Url, DateTimeOffset ExpiresAtUtc);
