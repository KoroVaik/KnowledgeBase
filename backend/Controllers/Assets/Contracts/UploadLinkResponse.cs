namespace Backend.Controllers.Assets.Contracts;

/// <param name="FileName">The key the bytes must be PUT under, and the one Confirm expects.</param>
/// <param name="Url">Where to PUT them.</param>
/// <param name="ContentType">Signed into the URL, so the PUT has to carry exactly this.</param>
/// <param name="ExpiresAtUtc">After this the bucket rejects the PUT.</param>
public sealed record UploadLinkResponse(
    string FileName,
    string Url,
    string ContentType,
    DateTimeOffset ExpiresAtUtc);
