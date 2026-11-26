// ============================================================================
// ไฟล์: Controllers/AuditLogsController.cs (ฝั่งหลังบ้าน - REST API ประวัติการทำรายการ)
// หน้าที่: ให้บริการดึงข้อมูลบันทึกประวัติการทำงานของระบบ (Audit Logs) สำหรับฝ่ายดูแลระบบและการเงิน
// เชื่อมต่อกับ:
//   - หน้าบ้าน: wwwroot/js/site.js ฟังก์ชัน loadAuditLogs() เรียก GET /api/audit-logs?role=...
//   - ฐานข้อมูล: ตาราง AuditLogs (AppDbContext.AuditLogs)
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// จำกัดสิทธิ์ให้เข้าถึงได้เฉพาะผู้ใช้ที่มีบทบาท Admin หรือ Finance เท่านั้น
[Authorize(Roles = $"{Roles.Admin},{Roles.Finance}")]
[Route("api/audit-logs")]
public class AuditLogsController(AppDbContext db) : ApiBaseController(db)
{
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/audit-logs?role={all|Admin|Finance|...}
    // ฝั่งหน้าบ้านเรียกมาจาก: wwwroot/js/site.js -> ฟังก์ชัน loadAuditLogs(roleFilter)
    // ข้อมูลที่รับ: Query parameter "role" (ไม่บังคับ) ใช้กรองตามบทบาทผู้ทำรายการ
    // ข้อมูลที่ส่งกลับ: JSON Array ของบันทึกประวัติล่าสุดไม่เกิน 150 รายการ เพื่อแสดงในตารางหน้า Admin/Finance
    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] string? role)
    {
        var query = Db.AuditLogs.AsQueryable();

        // ถ้าหน้าบ้านส่งตัวกรอง role มา (และไม่ใช่ "all") ให้กรองเฉพาะบทบาทนั้น
        if (!string.IsNullOrWhiteSpace(role) && role != "all")
            query = query.Where(a => a.ActorRole == role);

        // เรียงจากล่าสุดไปเก่าสุด จำกัด 150 รายการ เพื่อป้องกันข้อมูลเยอะเกินไป
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

        // ส่ง HTTP 200 OK พร้อมรายการ logs ในรูปแบบ JSON กลับไปหน้าบ้าน
        return Ok(logs);
    }
}
