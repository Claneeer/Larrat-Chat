using Larrat.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Larrat.Web.Controllers;

/// <summary>
/// Controller para upload/download de arquivos (suporta até 150MB via streaming).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IFileService fileService, ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/files/upload
    /// Faz upload de um arquivo e retorna o attachmentId para referenciar na mensagem.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(157_286_400)] // 150MB
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Nenhum arquivo enviado." });

        const long maxSize = 150L * 1024 * 1024;
        if (file.Length > maxSize)
            return BadRequest(new { error = $"Arquivo excede o limite de 150MB. Tamanho enviado: {file.Length / 1024 / 1024}MB" });

        try
        {
            var attachment = await _fileService.UploadAsync(file, userId);
            return Ok(new
            {
                id = attachment.Id,
                fileName = attachment.OriginalFileName,
                contentType = attachment.ContentType,
                sizeBytes = attachment.FileSizeBytes,
                category = attachment.Category.ToString(),
                audioDuration = attachment.AudioDurationSeconds,
                imageDimensions = attachment.ImageWidth.HasValue
                    ? new { width = attachment.ImageWidth, height = attachment.ImageHeight }
                    : null
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no upload de arquivo do usuário {UserId}", userId);
            return StatusCode(500, new { error = "Erro interno no upload." });
        }
    }

    /// <summary>
    /// GET /api/files/{id}/download
    /// Download de arquivo com Content-Disposition para forçar download.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        // TODO: verificar se o usuário tem acesso ao arquivo (membro do grupo/conversa)
        var attachment = await GetAttachmentById(id);
        if (attachment == null) return NotFound();

        var path = _fileService.GetAbsolutePath(attachment);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    /// <summary>
    /// GET /api/files/{id}/preview
    /// Serve o arquivo inline (para imagens/áudio/vídeo no browser).
    /// </summary>
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        var attachment = await GetAttachmentById(id);
        if (attachment == null) return NotFound();

        var path = _fileService.GetAbsolutePath(attachment);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers.Append("Cache-Control", "private, max-age=3600");
        return File(stream, attachment.ContentType);
    }

    private async Task<Larrat.Core.Entities.FileAttachment?> GetAttachmentById(Guid id)
    {
        // Acessa diretamente o DbContext via DI para não criar dependência circular
        var db = HttpContext.RequestServices.GetRequiredService<Larrat.Core.Data.LarratDbContext>();
        return await db.FileAttachments.FindAsync(id);
    }
}
