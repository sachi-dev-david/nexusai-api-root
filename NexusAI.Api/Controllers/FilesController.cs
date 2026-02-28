using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;
using NexusAI.Api.Services;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 檔案上傳 API
/// POST /api/files/upload — 上傳附件，回傳 fileId 供 chat/stream 使用
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ILogger<FilesController> _logger;

    // 允許的檔案類型
    private static readonly string[] AllowedContentTypes =
    [
        "application/pdf",
        "image/jpeg", "image/png", "image/webp",
        "text/plain", "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", // xlsx
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" // docx
    ];

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    public FilesController(IFileService fileService, ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    /// <summary>上傳附件</summary>
    /// <remarks>
    /// Content-Type: multipart/form-data
    /// Form field: file（IFormFile）
    ///
    /// 檔案暫存 24 小時後自動清除。
    /// 回傳的 fileId 需於 POST /api/chat/stream 的 fileId 欄位帶入。
    /// </remarks>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        // 驗證檔案
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("請選擇要上傳的檔案"));

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(ApiResponse.Fail("檔案大小不可超過 20MB"));

        if (!AllowedContentTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(ApiResponse.Fail($"不支援的檔案類型：{file.ContentType}"));

        var userId = GetUserId();

        try
        {
            var result = await _fileService.UploadAsync(file, userId);
            _logger.LogInformation("使用者 {UserId} 上傳檔案 {FileName}", userId, file.FileName);
            return Ok(ApiResponse<FileUploadResponse>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "檔案上傳失敗");
            return StatusCode(500, ApiResponse.Fail("檔案上傳失敗，請稍後再試"));
        }
    }

    private string GetUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException();
}
