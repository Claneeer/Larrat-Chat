namespace Larrat.Core.Entities;

public class FileAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty; // GUID no disco
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string UploaderUserId { get; set; } = string.Empty;
    public ApplicationUser? Uploader { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string StoragePath { get; set; } = string.Empty;  // Caminho relativo no disco
    public FileCategory Category { get; set; } = FileCategory.Other;

    // Metadados de mídia (preenchidos no upload)
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public double? AudioDurationSeconds { get; set; }
    public double? VideoDurationSeconds { get; set; }
    public string? ThumbnailPath { get; set; }

    // SHA-256 do arquivo original para verificação de integridade
    public string? ChecksumSha256 { get; set; }
}

public enum FileCategory
{
    Image,
    Audio,
    Video,
    Document,
    Other
}
