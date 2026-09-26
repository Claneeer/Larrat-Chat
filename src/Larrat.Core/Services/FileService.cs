using Larrat.Core.Data;
using Larrat.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace Larrat.Core.Services;

public interface IFileService
{
    /// <summary>Faz upload de um arquivo (máx 150MB) e retorna o FileAttachment salvo.</summary>
    Task<FileAttachment> UploadAsync(IFormFile file, string uploaderUserId);

    /// <summary>Retorna o caminho absoluto do arquivo no disco.</summary>
    string GetAbsolutePath(FileAttachment attachment);

    /// <summary>Verifica SHA-256 de integridade.</summary>
    Task<bool> VerifyChecksumAsync(Guid attachmentId);
}

public class FileService : IFileService
{
    private const long MaxFileSizeBytes = 150L * 1024 * 1024; // 150 MB
    private readonly LarratDbContext _db;
    private readonly string _uploadRoot;

    public FileService(LarratDbContext db, IConfiguration config)
    {
        _db = db;
        _uploadRoot = config["FileStorage:UploadRoot"] ?? "uploads";
        Directory.CreateDirectory(_uploadRoot);
    }

    public async Task<FileAttachment> UploadAsync(IFormFile file, string uploaderUserId)
    {
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"Arquivo excede o limite de 150MB. Tamanho: {file.Length / 1024 / 1024}MB");

        var storedFileName = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName);
        var category = GetFileCategory(file.ContentType);
        var relativePath = Path.Combine(category.ToString().ToLower(), storedFileName);
        var absolutePath = Path.Combine(_uploadRoot, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // Salva o arquivo e calcula SHA-256 ao mesmo tempo (streaming)
        string checksum;
        using (var sha256 = SHA256.Create())
        using (var fileStream = new FileStream(absolutePath, FileMode.Create))
        using (var cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write))
        {
            await file.CopyToAsync(cryptoStream);
            await cryptoStream.FlushFinalBlockAsync();
            checksum = Convert.ToHexString(sha256.Hash!).ToLower();
        }

        var attachment = new FileAttachment
        {
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploaderUserId = uploaderUserId,
            StoragePath = relativePath,
            Category = category,
            ChecksumSha256 = checksum
        };

        _db.FileAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        return attachment;
    }

    public string GetAbsolutePath(FileAttachment attachment)
        => Path.Combine(_uploadRoot, attachment.StoragePath);

    public async Task<bool> VerifyChecksumAsync(Guid attachmentId)
    {
        var attachment = await _db.FileAttachments.FindAsync(attachmentId);
        if (attachment == null) return false;

        var path = GetAbsolutePath(attachment);
        if (!File.Exists(path)) return false;

        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLower();
        return hash == attachment.ChecksumSha256;
    }

    private static FileCategory GetFileCategory(string contentType)
    {
        if (contentType.StartsWith("image/")) return FileCategory.Image;
        if (contentType.StartsWith("audio/")) return FileCategory.Audio;
        if (contentType.StartsWith("video/")) return FileCategory.Video;
        if (contentType == "application/pdf" || contentType.Contains("document") || contentType.StartsWith("text/"))
            return FileCategory.Document;
        return FileCategory.Other;
    }
}
