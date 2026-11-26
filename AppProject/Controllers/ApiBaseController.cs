// ============================================================================
// ไฟล์: Controllers/ApiBaseController.cs (ฝั่งหลังบ้าน - คลาสแม่ของ API Controller ทุกตัว)
// หน้าที่: รวมเมธอดและตัวช่วยพื้นฐานที่ใช้ร่วมกันในทุก API Controller ได้แก่
//         1. การเข้าถึงฐานข้อมูล (Db)
//         2. การอ่านตัวตนผู้ใช้ปัจจุบันจาก Cookie Authentication (CurrentUserId / GetCurrentActiveUserAsync)
//         3. การบันทึกประวัติการทำงาน (AuditLog)
//         4. การแปลงข้อมูล User เป็น JSON อย่างปลอดภัย (ไม่ส่ง PasswordHash กลับไปหน้าบ้าน)
// เชื่อมต่อกับ:
//   - สืบทอดโดย: AuthController, AuctionsController, SellerController, FinanceController,
//               AdminController, ChatController, NotificationsController, AuditLogsController
// ============================================================================

using System.Security.Claims;
using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// [ApiController] เปิดใช้งานฟีเจอร์ REST API อัตโนมัติ (เช่น ตรวจสอบ ModelState และอ่าน JSON Body อัตโนมัติ)
[ApiController]
public abstract class ApiBaseController(AppDbContext db) : ControllerBase
{
    // อินสแตนซ์ฐานข้อมูลสำหรับให้ Controller ลูกนำไป Query/Save ข้อมูล
    protected readonly AppDbContext Db = db;

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // ดึงรหัสผู้ใช้ (User.Id) จาก Claims ใน Cookie "OldThingsAuth" ที่หน้าบ้านแนบมากับ HTTP Request
    // คืนค่า null หากยังไม่ได้เข้าสู่ระบบ
    protected int? CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    // ดึงข้อมูลผู้ใช้ปัจจุบันจากฐานข้อมูลจริง พร้อมตรวจว่าบัญชีถูกระงับ (IsSuspended) หรือไม่
    // หากถูกระงับระหว่างใช้งาน จะบังคับ SignOut ล้าง Cookie ทันทีเพื่อตัดสิทธิ์การใช้งานจากหน้าบ้าน
    protected async Task<User?> GetCurrentActiveUserAsync()
    {
        if (CurrentUserId is not int id) return null;
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return null;
        if (user.IsSuspended)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }
        return user;
    }

    // บันทึกประวัติการกระทำสำคัญลงตาราง AuditLogs (เช่น แอดมินอนุมัติสินค้า, การเงินโอนเงินให้ผู้ขาย)
    // หมายเหตุ: แค่ Add เข้า DbContext ยังไม่ SaveChangesAsync (จะถูกบันทึกพร้อมธุรกรรมหลัก)
    protected void AddAuditLog(User actor, string action, string targetType, int? targetId, string details)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            ActorId = actor.Id,
            ActorName = actor.DisplayName,
            ActorRole = actor.Role,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            // ตัดความยาวไม่ให้เกิน 600 ตัวอักษรตามข้อจำกัดของคอลัมน์ในฐานข้อมูล
            Details = details.Length > 600 ? details[..600] : details,
            CreatedAt = DateTime.UtcNow
        });
    }

    // 🔗 [จุดเชื่อมต่อ หลังบ้าน -> หน้าบ้าน]
    // แปลงออบเจ็กต์ User เป็นโครงสร้างข้อมูลสรุป (DTO แบบ Anonymous) สำหรับส่งเป็น JSON กลับไปหน้าบ้าน
    // จงใจคัดเฉพาะฟิลด์ที่ปลอดภัย โดยไม่ส่ง PasswordHash, PasswordResetToken หรือข้อมูลลับออกไป
    protected static object MapUserSummary(User u) => new
    {
        u.Id,
        u.DisplayName,
        u.Email,
        u.Role,
        u.IsSuspended,
        u.SuspendReason,
        u.SellerStatus,
        u.ShopName,
        u.Phone,
        u.BankAccount,
        u.SellerBio,
        u.SellerRejectReason,
        u.SellerAppliedAt,
        u.CreatedAt
    };
}
