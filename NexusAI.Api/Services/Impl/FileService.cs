using NexusAI.Api.Data;
using NexusAI.Api.Data.Entities;
using NexusAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace NexusAI.Api.Services.Impl;

public class FileService : IFileService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<FileService> _logger;

    public FileService(AppDbContext db, IConfiguration config, ILogger<FileService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    public async Task<FileUploadResponse> UploadAsync(IFormFile file, string userId)
    {
        var uid         = int.Parse(userId);
        var tempPath    = _config["FileStorage:TempPath"] ?? "uploads/temp";
        var expireHours = _config.GetValue<int>("FileStorage:ExpireHours", 24);

        Directory.CreateDirectory(tempPath);

        var fileId    = $"file_{Guid.NewGuid():N}";
        var extension = Path.GetExtension(file.FileName);
        var savePath  = Path.Combine(tempPath, $"{fileId}{extension}");

        await using (var stream = new FileStream(savePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var expiresAt = DateTime.UtcNow.AddHours(expireHours);

        // 寫入 DB
        _db.UploadedFiles.Add(new UploadedFileEntity
        {
            Id           = fileId,
            UserId       = uid,
            OriginalName = file.FileName,
            SavedPath    = savePath,
            ContentType  = file.ContentType,
            SizeBytes    = file.Length,
            UploadedAt   = DateTime.UtcNow,
            ExpiresAt    = expiresAt,
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("檔案上傳：{FileId} | {Name} | userId={UserId}", fileId, file.FileName, userId);

        return new FileUploadResponse
        {
            FileId    = fileId,
            FileName  = file.FileName,
            SizeBytes = file.Length,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>取得暫存檔案實體路徑（供 ChatService 讀取附件）</summary>
    public async Task<string?> GetFilePathAsync(string fileId, string userId)
    {
        var uid  = int.Parse(userId);
        var file = await _db.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == uid && f.ExpiresAt > DateTime.UtcNow);

        return file?.SavedPath;
    }
}
