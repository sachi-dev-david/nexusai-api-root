using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;

namespace NexusAI.Api.Controllers;

/// <summary>
/// Skills 查詢 API
/// GET /api/skills — 取得可用 Skill 列表（供前端面板顯示）
/// </summary>
[ApiController]
[Route("api/skills")]
[Authorize]
public class SkillsController : ControllerBase
{
    // Skill 列表為靜態設定，未來可改為從 DB 或 SK Plugin 動態載入
    private static readonly List<SkillItem> _skills =
    [
        new() { Id = "s1", Name = "get_machine_status",   Label = "設備狀態查詢", Icon = "⚙️", Color = "#76b900" },
        new() { Id = "s2", Name = "query_rag",            Label = "知識庫查詢",   Icon = "📚", Color = "#9b5de5" },
        new() { Id = "s3", Name = "query_quote",          Label = "報價查詢",     Icon = "🔍", Color = "#00b4d8" },
        new() { Id = "s4", Name = "query_quote_summary",  Label = "報價統計",     Icon = "📊", Color = "#f77f00" },
        new() { Id = "s5", Name = "query_process",        Label = "製程查詢",     Icon = "🔧", Color = "#f15bb5" },
    ];

    /// <summary>取得可用 Skill 列表</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SkillItem>>), 200)]
    public IActionResult GetSkills()
    {
        return Ok(ApiResponse<List<SkillItem>>.Ok(_skills));
    }
}
