using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = $"{Roles.Admin},{Roles.Finance}")]
[Route("api/audit-logs")]
public class AuditLogsController(AppDbContext db) : ApiBaseController(db)
{
    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] string? role)
    {
        var query = Db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(role) && role != "all")
            query = query.Where(a => a.ActorRole == role);

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(150)
            .Select(a => new
            {
                a.Id,
                a.ActorId,
                a.ActorName,
                a.ActorRole,
                a.Action,
                a.TargetType,
                a.TargetId,
                a.Details,
                a.CreatedAt
            })
            .ToListAsync();

        return Ok(logs);
    }
}
