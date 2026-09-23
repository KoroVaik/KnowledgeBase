using System.ComponentModel.DataAnnotations;

namespace KnowledgeBase.Api.Controllers.Assets.Contracts;

public sealed record AssetBatchRequest<T>([Required, MinLength(1), MaxLength(50)] BatchItem<T>[] Items);
public sealed record BatchItem<T>([Required, StringLength(80, MinimumLength = 1), RegularExpression("^[a-zA-Z0-9_.-]+$")] string Id, [Required] T Request,
    [StringLength(80)] string? UploadBatchId = null, [StringLength(55)] string? TraceParent = null);
public sealed record AssetBatchResult<T>(string Id, int Status, T? Value, string? Error);
public sealed record DownloadLinkRequest([Required, StringLength(64)] string FileName);
public sealed record ConfirmBatchItem(
    [Required, StringLength(64)] string FileName,
    [StringLength(255)] string? OriginalFileName,
    [StringLength(255)] string? ContentType);
