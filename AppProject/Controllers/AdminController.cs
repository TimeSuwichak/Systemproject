// =================================================================================================
// ไฟล์: AppProject/Controllers/AdminController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend - REST API Controller) สำหรับระบบ "ผู้ดูแลระบบ (Admin)"
// หน้าที่โดยรวม:
//   - ดึงข้อมูลแดชบอร์ดแอดมินทั้งหมด (สถิติภาพรวม, คิวอนุมัติผู้ขาย, คิวอนุมัติสินค้า, รายชื่อผู้ใช้,
//     หมวดหมู่สินค้า, ข้อพิพาทที่ร้องเรียน, และรายการผู้ชนะประมูลแบบอ่านอย่างเดียว)
//   - อนุมัติหรือปฏิเสธใบสมัครผู้ขาย (พร้อมเลื่อนสิทธิ์ Role เป็น Seller และส่ง Notification)
//   - อนุมัติหรือปฏิเสธสินค้าประมูลที่ผู้ขายส่งเข้ามา (พร้อมส่ง Notification และปรับเวลาปิดอัตโนมัติถ้าเลยกำหนดแล้ว)
//   - ระงับหรือปลดระงับบัญชีผู้ใช้ (ป้องกันการระงับบัญชีตัวเอง)
//   - จัดการหมวดหมู่สินค้า (เพิ่ม แก้ไข และลบ/ปิดการใช้งานอัตโนมัติกรณีมีสินค้าผูกอยู่)
//   - ตรวจสอบประวัติแชทเฉพาะห้องที่มีการแจ้งข้อพิพาท (Dispute) และอัปเดตสถานะการไกล่เกลี่ยข้อพิพาท
//   - บันทึก Audit Log ทุกการกระทำของแอดมินลงฐานข้อมูลเพื่อตรวจสอบย้อนหลังได้
// การเชื่อมต่อกับไฟล์/เลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): เชื่อมต่อจาก wwwroot/js/site.js ผ่านฟังก์ชัน api() (ครอบ fetch())
//     โดยเบราว์เซอร์แนบ Cookie ยืนยันตัวตน "OldThingsAuth" (สิทธิ์ Role = Admin)
//     และแสดงผลใน Views/Home/Index.cshtml (หน้า #view-admin-approvals, #view-admin-users,
//     #view-admin-categories, #view-admin-disputes, #view-admin-winners)
//   - ฝั่งหลังบ้าน (Backend): สืบทอดจาก ApiBaseController (ใช้ GetCurrentActiveUserAsync, MapUserSummary, AddAuditLog)
//     และเชื่อมต่อฐานข้อมูลผ่าน AppDbContext
// =================================================================================================

using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// กำหนดสิทธิ์ระดับคลาส: อนุญาตเฉพาะผู้ใช้ที่มี Role = "Admin" เท่านั้น
[Authorize(Roles = Roles.Admin)]
// กำหนดเส้นทางหลัก (Base Route) ของ Controller นี้เป็น /api/admin
[Route("api/admin")]
public class AdminController(AppDbContext db) : ApiBaseController(db)
{
    // DTO สำหรับรับผลการพิจารณาใบสมัครผู้ขาย (อนุมัติหรือปฏิเสธพร้อมเหตุผล)
    public record ReviewSellerRequest(bool Approve, string? Reason);

    // DTO สำหรับรับผลการพิจารณาอนุมัติสินค้าประมูล (อนุมัติหรือปฏิเสธพร้อมเหตุผล)
    public record ReviewAuctionRequest(bool Approve, string? Reason);

    // DTO สำหรับรับคำสั่งระงับ (Suspend = true) หรือปลดระงับ (Suspend = false) บัญชีผู้ใช้
    public record SuspendUserRequest(bool Suspend, string? Reason);

    // DTO สำหรับเพิ่มหรือแก้ไขหมวดหมู่สินค้า (ค่าเริ่มต้น IsActive = true)
    public record CategoryRequest(string Name, string? Symbol, string? Description, bool IsActive = true);

    // DTO สำหรับอัปเดตสถานะข้อพิพาท (Open / Investigating / Resolved) และบันทึกหมายเหตุของแอดมิน
    public record UpdateDisputeRequest(string Status, string? AdminNote);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : GET /api/admin/dashboard
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟังก์ชัน loadAdminDashboard()
    //                        แสดงผลใน Views/Home/Index.cshtml หน้า #view-admin-approvals, #view-admin-users,
    //                        #view-admin-categories, #view-admin-disputes และ #view-admin-winners
    // ข้อมูลที่รับจากหน้าบ้าน : ไม่มีพารามิเตอร์ (ยืนยันตัวตนผ่าน Cookie "OldThingsAuth", สิทธิ์ Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON ประกอบด้วยสถิติภาพรวม (stats), คิวผู้ขายรออนุมัติ (pendingSellers),
    //                        คิวสินค้ารออนุมัติ (pendingAuctions), รายชื่อผู้ใช้ทั้งหมด (users),
    //                        หมวดหมู่พร้อมจำนวนสินค้า (categories), รายการข้อพิพาท (disputes),
    //                        และรายการผู้ชนะประมูลแบบอ่านอย่างเดียว (winnersReadOnly)
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var now = DateTime.UtcNow;

        // ดึงรายการผู้ใช้ที่สมัครเป็นผู้ขายและกำลังรอแอดมินตรวจสอบ เรียงจากผู้สมัครล่าสุด
        var pendingSellers = await Db.Users
            .Where(u => u.SellerStatus == SellerStatuses.Pending)
            .OrderByDescending(u => u.SellerAppliedAt)
            .ToListAsync();

        // ดึงรายการสินค้าประมูลที่อยู่ในสถานะรออนุมัติ (Pending) พร้อมข้อมูลหมวดหมู่และผู้ขาย
        var pendingAuctions = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Where(a => a.Status == AuctionStatuses.Pending)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        // นับจำนวนรายการประมูลที่ได้รับอนุมัติแล้วและกำลังเปิดให้บิดอยู่ในขณะนี้
        var liveCount = await Db.AuctionItems.CountAsync(a => a.Status == AuctionStatuses.Approved && a.EndAt > now);

        // ดึงรายชื่อบัญชีผู้ใช้ทั้งหมดในระบบสำหรับหน้าจัดการผู้ใช้ (#view-admin-users)
        var users = await Db.Users
            .OrderBy(u => u.Id)
            .ToListAsync();

        // ดึงหมวดหมู่สินค้าทั้งหมด (รวมทั้งที่ปิดใช้งานแล้ว) สำหรับหน้าจัดการหมวดหมู่ (#view-admin-categories)
        var categories = await Db.Categories
            .OrderBy(c => c.Id)
            .ToListAsync();

        // นับจำนวนสินค้าที่ผูกอยู่กับแต่ละหมวดหมู่ เพื่อแสดงให้แอดมินเห็นก่อนตัดสินใจลบ/ปิดใช้งาน
        var itemCountsByCategory = await Db.AuctionItems
            .GroupBy(a => a.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // ดึงรายการข้อพิพาททั้งหมดพร้อมข้อมูลผู้ร้องเรียน คำสั่งซื้อ ห้องแชท สินค้า ผู้ซื้อ และผู้ขาย
        var disputes = await Db.Disputes
            .Include(d => d.Reporter)
            .Include(d => d.OrderPayment)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.AuctionItem)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Buyer)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Seller)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        // ดึงรายการผู้ชนะประมูล (OrderPayments) แบบอ่านอย่างเดียวเพื่อให้แอดมินติดตามภาพรวมได้โดยไม่ก้าวก่ายงานการเงิน
        var winners = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
            // สรุปตัวเลขสถิติสำหรับแสดงในการ์ด #adminStatsCards
            stats = new
            {
                pendingSellersCount = pendingSellers.Count,
                pendingAuctionsCount = pendingAuctions.Count,
                liveAuctionsCount = liveCount,
                openDisputesCount = disputes.Count(d => d.Status != DisputeStatuses.Resolved),
                totalUsersCount = users.Count,
                suspendedUsersCount = users.Count(u => u.IsSuspended),
                winnersCount = winners.Count
            },
            pendingSellers = pendingSellers.Select(MapUserSummary),
            pendingAuctions = pendingAuctions.Select(a => new
            {
                a.Id,
                a.Name,
                a.CategoryId,
                categoryName = a.Category?.Name ?? "",
                sellerName = a.Seller?.ShopName ?? a.Seller?.DisplayName ?? "",
                sellerEmail = a.Seller?.Email ?? "",
                a.Condition,
                a.Description,
                a.ImageUrl,
                a.StartPrice,
                a.MinIncrement,
                a.StartAt,
                a.EndAt,
                a.CreatedAt
            }),
            users = users.Select(MapUserSummary),
            categories = categories.Select(c => new
            {
                c.Id,
                c.Name,
                c.Symbol,
                c.Description,
                c.IsActive,
                itemCount = itemCountsByCategory.GetValueOrDefault(c.Id, 0)
            }),
            disputes = disputes.Select(d => new
            {
                d.Id,
                d.ConversationId,
                d.OrderPaymentId,
                orderCode = d.OrderPayment?.OrderCode,
                itemName = d.Conversation?.AuctionItem?.Name ?? "",
                buyerName = d.Conversation?.Buyer?.DisplayName ?? "",
                sellerName = d.Conversation?.Seller?.ShopName ?? d.Conversation?.Seller?.DisplayName ?? "",
                reporterName = d.Reporter?.DisplayName ?? "",
                d.Reason,
                d.Details,
                d.Status,
                d.AdminNote,
                d.CreatedAt,
                d.ResolvedAt
            }),
            winnersReadOnly = winners.Select(o => new
            {
                o.Id,
                o.OrderCode,
                o.AuctionItemId,
                itemName = o.AuctionItem?.Name ?? "",
                categoryName = o.AuctionItem?.Category?.Name ?? "",
                imageUrl = o.AuctionItem?.ImageUrl ?? "",
                winnerName = o.Buyer?.DisplayName ?? "",
                sellerName = o.Seller?.ShopName ?? o.Seller?.DisplayName ?? "",
                o.WinningPrice,
                o.FinalBuyerAmount,
                o.PaymentMethod,
                o.PaymentStatus,
                o.MeetupStatus,
                o.CreatedAt
            })
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/admin/sellers/{userId}/review
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-approve-seller] (ส่ง approve: true)
    //                        และปุ่ม [data-reject-seller] (เปิด Modal กรอกเหตุผล ส่ง approve: false, reason)
    //                        ในหน้า #view-admin-approvals
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {userId:int} + JSON Body (ReviewSellerRequest: approve, reason)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (ปรับ Role/SellerStatus, ส่ง Notification ให้ผู้ใช้, ลง AuditLog)
    //                        หรือ 400 BadRequest (สถานะไม่ใช่ Pending / ไม่กรอกเหตุผลปฏิเสธ), 401 Unauthorized, 404 NotFound
    [HttpPost("sellers/{userId:int}/review")]
    public async Task<IActionResult> ReviewSeller(int userId, [FromBody] ReviewSellerRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        // ค้นหาบัญชีผู้สมัครและตรวจว่ากำลังอยู่ในสถานะรออนุมัติ (Pending) จริง
        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (target is null) return NotFound(new { message = "ไม่พบบัญชีผู้ใช้" });
        if (target.SellerStatus != SellerStatuses.Pending)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอตรวจสอบ" });

        if (req.Approve)
        {
            // กรณีอนุมัติ: เลื่อนบทบาท (Role) เป็น Seller และเปลี่ยนสถานะใบสมัครเป็น Approved
            target.Role = Roles.Seller;
            target.SellerStatus = SellerStatuses.Approved;
            target.SellerRejectReason = null;
            // แจ้งเตือนผู้ใช้ให้ทราบว่าร้านได้รับอนุมัติแล้วและกดไปยังหน้าลงขายสินค้า ("sell") ได้เลย
            Db.Notifications.Add(new Notification
            {
                UserId = target.Id,
                Type = "seller",
                Title = "ใบสมัครผู้ขายได้รับการอนุมัติแล้ว",
                Body = $"ยินดีด้วย! ร้าน “{target.ShopName}” ได้รับการอนุมัติแล้ว คุณสามารถลงขายสินค้าได้ทันที",
                TargetView = "sell",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            // บันทึก Audit Log การอนุมัติผู้ขาย
            AddAuditLog(admin, "ApproveSeller", "User", target.Id, $"อนุมัติผู้ขาย {target.DisplayName} (ร้าน {target.ShopName})");
        }
        else
        {
            // กรณีปฏิเสธ: บังคับระบุเหตุผลเพื่อให้ผู้สมัครนำไปแก้ไขและส่งสมัครใหม่ได้
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธใบสมัครผู้ขาย" });
            target.SellerStatus = SellerStatuses.Rejected;
            target.SellerRejectReason = req.Reason.Trim();
            // ส่งการแจ้งเตือนพร้อมกำหนด TargetView ให้พาไปยังหน้าส่งสมัครใหม่ที่ตรงกับ Role ปัจจุบัน
            Db.Notifications.Add(new Notification
            {
                UserId = target.Id,
                Type = "seller",
                Title = "ใบสมัครผู้ขายไม่ผ่านการอนุมัติ",
                Body = $"เหตุผล: {target.SellerRejectReason}",
                TargetView = target.Role == Roles.Seller ? "sell" : "apply-seller",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            // บันทึก Audit Log การปฏิเสธผู้ขาย
            AddAuditLog(admin, "RejectSeller", "User", target.Id, $"ปฏิเสธผู้ขาย {target.DisplayName}: {target.SellerRejectReason}");
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = req.Approve ? $"อนุมัติผู้ขาย “{target.DisplayName}” แล้ว" : $"ปฏิเสธใบสมัครผู้ขาย “{target.DisplayName}” แล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/admin/auctions/{itemId}/review
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-approve-auction] (ส่ง approve: true)
    //                        และปุ่ม [data-reject-auction] (เปิด Modal กรอกเหตุผล ส่ง approve: false, reason)
    //                        ในหน้า #view-admin-approvals
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {itemId:int} + JSON Body (ReviewAuctionRequest: approve, reason)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (ปรับสถานะสินค้า Approved/Rejected, แจ้งเตือนผู้ขาย, ลง AuditLog)
    //                        หรือ 400 BadRequest (สถานะไม่ใช่ Pending / ไม่กรอกเหตุผลปฏิเสธ), 401 Unauthorized, 404 NotFound
    [HttpPost("auctions/{itemId:int}/review")]
    public async Task<IActionResult> ReviewAuction(int itemId, [FromBody] ReviewAuctionRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var item = await Db.AuctionItems.FirstOrDefaultAsync(a => a.Id == itemId);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้า" });
        if (item.Status != AuctionStatuses.Pending)
            return BadRequest(new { message = "สินค้านี้ไม่ได้อยู่ในสถานะรออนุมัติ" });

        if (req.Approve)
        {
            // กรณีอนุมัติ: เปลี่ยนสถานะเป็น Approved เพื่อให้ไปปรากฏบนหน้าประมูลวันนี้
            item.Status = AuctionStatuses.Approved;
            item.RejectReason = null;
            // หากเวลาปิดประมูลที่ผู้ขายตั้งไว้เลยเวลาปัจจุบันไปแล้วระหว่างรออนุมัติ ให้ยืดเวลาปิดออกไปอีก 24 ชั่วโมงอัตโนมัติ
            if (item.EndAt <= DateTime.UtcNow)
                item.EndAt = DateTime.UtcNow.AddHours(24);
            // แจ้งเตือนผู้ขายว่าสินค้าผ่านการอนุมัติเปิดประมูลแล้ว
            Db.Notifications.Add(new Notification
            {
                UserId = item.SellerId,
                Type = "auction",
                Title = "สินค้าได้รับการอนุมัติเปิดประมูลแล้ว",
                Body = $"สินค้า “{item.Name}” ผ่านการตรวจสอบและเริ่มแสดงในรายการประมูลแล้ว",
                TargetView = "sell",
                ReferenceId = item.Id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            // บันทึก Audit Log การอนุมัติสินค้า
            AddAuditLog(admin, "ApproveAuction", "AuctionItem", item.Id, $"อนุมัติสินค้า “{item.Name}” เริ่ม ฿{item.StartPrice:N0}");
        }
        else
        {
            // กรณีปฏิเสธ: บังคับกรอกเหตุผลเพื่อให้ผู้ขายกลับมาแก้ไขและส่งตรวจใหม่ได้
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธสินค้า" });
            item.Status = AuctionStatuses.Rejected;
            item.RejectReason = req.Reason.Trim();
            // แจ้งเตือนผู้ขายพร้อมเหตุผลที่ถูกปฏิเสธ
            Db.Notifications.Add(new Notification
            {
                UserId = item.SellerId,
                Type = "auction",
                Title = "สินค้าถูกปฏิเสธการอนุมัติ",
                Body = $"สินค้า “{item.Name}” — เหตุผล: {item.RejectReason}",
                TargetView = "sell",
                ReferenceId = item.Id,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            // บันทึก Audit Log การปฏิเสธสินค้า
            AddAuditLog(admin, "RejectAuction", "AuctionItem", item.Id, $"ปฏิเสธสินค้า “{item.Name}”: {item.RejectReason}");
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = req.Approve ? $"อนุมัติสินค้า “{item.Name}” เข้าสู่รายการประมูลแล้ว" : $"ปฏิเสธสินค้า “{item.Name}” พร้อมเหตุผลแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/admin/users/{userId}/suspend
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-suspend-user] (เปิด Modal กรอกเหตุผล ส่ง suspend: true, reason)
    //                        และปุ่ม [data-unsuspend-user] (ส่ง suspend: false) ในหน้า #view-admin-users
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {userId:int} + JSON Body (SuspendUserRequest: suspend, reason)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (อัปเดต IsSuspended/SuspendReason และลง AuditLog)
    //                        หรือ 400 BadRequest (พยายามระงับตัวเอง / ไม่ระบุเหตุผลตอนระงับ), 401 Unauthorized, 404 NotFound
    [HttpPost("users/{userId:int}/suspend")]
    public async Task<IActionResult> SuspendUser(int userId, [FromBody] SuspendUserRequest req)
    {
        var me = await GetCurrentActiveUserAsync();
        if (me is null) return Unauthorized();
        // ป้องกันแอดมินกดระงับบัญชีของตนเองโดยไม่ตั้งใจ
        if (me.Id == userId)
            return BadRequest(new { message = "ไม่สามารถระงับบัญชีตัวเองได้" });

        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (target is null) return NotFound(new { message = "ไม่พบบัญชีผู้ใช้" });

        // หากเป็นการสั่งระงับ (Suspend = true) บังคับต้องระบุเหตุผล
        if (req.Suspend && string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "กรุณาระบุเหตุผลในการระงับบัญชี" });

        // อัปเดตสถานะการระงับบัญชี (เมื่อปลดระงับจะล้าง SuspendReason ออกด้วย)
        target.IsSuspended = req.Suspend;
        target.SuspendReason = req.Suspend ? req.Reason!.Trim() : null;
        // บันทึก Audit Log ทั้งกรณีระงับและปลดระงับบัญชี
        AddAuditLog(me, req.Suspend ? "SuspendUser" : "UnsuspendUser", "User", target.Id,
            req.Suspend ? $"ระงับบัญชี {target.Email}: {target.SuspendReason}" : $"ปลดระงับบัญชี {target.Email}");
        await Db.SaveChangesAsync();

        return Ok(new { message = req.Suspend ? $"ระงับบัญชี {target.DisplayName} แล้ว" : $"ปลดระงับบัญชี {target.DisplayName} แล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/admin/categories
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "submit" ของฟอร์ม #adminCategoryForm (กรณีไม่มีฟิลด์ id = โหมดเพิ่มใหม่)
    //                        ในหน้า #view-admin-categories
    // ข้อมูลที่รับจากหน้าบ้าน : JSON Body (CategoryRequest: name, symbol, description, isActive) + Cookie "OldThingsAuth" (Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message, category } (สร้างหมวดหมู่ใหม่และลง AuditLog)
    //                        หรือ 400 BadRequest (ไม่ระบุชื่อ), 401 Unauthorized, 409 Conflict (ชื่อหมวดหมู่ซ้ำ)
    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "กรุณาระบุชื่อหมวดหมู่" });
        // ตรวจสอบไม่ให้มีชื่อหมวดหมู่ซ้ำกัน (เปรียบเทียบแบบไม่สนตัวพิมพ์เล็ก-ใหญ่)
        if (await Db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower()))
            return Conflict(new { message = "มีชื่อหมวดหมู่นี้อยู่แล้ว" });

        // สร้างหมวดหมู่ใหม่ (ถ้าไม่ใส่สัญลักษณ์ ให้ใช้ "✦" เป็นค่าเริ่มต้น)
        var cat = new Category
        {
            Name = name,
            Symbol = string.IsNullOrWhiteSpace(req.Symbol) ? "✦" : req.Symbol.Trim(),
            Description = req.Description?.Trim(),
            IsActive = req.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        Db.Categories.Add(cat);
        // บันทึกครั้งแรกก่อนเพื่อให้ฐานข้อมูลสร้างค่า cat.Id แล้วจึงบันทึก Audit Log ที่มี EntityId ครบถ้วน
        await Db.SaveChangesAsync();
        AddAuditLog(admin, "CreateCategory", "Category", cat.Id, $"เพิ่มหมวดหมู่ “{cat.Name}”");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"เพิ่มหมวดหมู่ “{cat.Name}” แล้ว", category = cat });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : PUT /api/admin/categories/{id}
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "submit" ของฟอร์ม #adminCategoryForm (กรณีมีฟิลด์ id = โหมดแก้ไข)
    //                        ในหน้า #view-admin-categories
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {id:int} + JSON Body (CategoryRequest: name, symbol, description, isActive)
    //                        + Cookie "OldThingsAuth" (Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (อัปเดตหมวดหมู่และลง AuditLog)
    //                        หรือ 400 BadRequest, 401 Unauthorized, 404 NotFound, 409 Conflict (ชื่อซ้ำกับหมวดอื่น)
    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var cat = await Db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound(new { message = "ไม่พบหมวดหมู่" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "กรุณาระบุชื่อหมวดหมู่" });
        // ตรวจสอบชื่อซ้ำเฉพาะกับหมวดหมู่อื่นที่มี Id ไม่ตรงกับรายการที่กำลังแก้ไข
        if (await Db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower()))
            return Conflict(new { message = "มีชื่อหมวดหมู่นี้อยู่แล้ว" });

        // อัปเดตข้อมูลหมวดหมู่ (หากไม่ได้ส่ง Symbol ใหม่มา ให้คง Symbol เดิมไว้)
        cat.Name = name;
        cat.Symbol = string.IsNullOrWhiteSpace(req.Symbol) ? cat.Symbol : req.Symbol.Trim();
        cat.Description = req.Description?.Trim();
        cat.IsActive = req.IsActive;
        AddAuditLog(admin, "UpdateCategory", "Category", cat.Id, $"แก้ไขหมวดหมู่ “{cat.Name}” (Active={cat.IsActive})");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"อัปเดตหมวดหมู่ “{cat.Name}” แล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : DELETE /api/admin/categories/{id}
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-delete-cat] ในหน้า #view-admin-categories
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {id:int} + Cookie "OldThingsAuth" (Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message }
    //                        - หากมีสินค้าผูกอยู่กับหมวดหมู่นี้: ปิดการใช้งาน (Soft Deactivate: IsActive = false) แทนการลบจริงเพื่อรักษาความสมบูรณ์ของข้อมูล
    //                        - หากไม่มีสินค้าผูกอยู่: ลบออกจากฐานข้อมูลจริง (Hard Delete) พร้อมลง AuditLog
    //                        หรือ 401 Unauthorized, 404 NotFound
    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var cat = await Db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound(new { message = "ไม่พบหมวดหมู่" });

        // ตรวจสอบว่ามีสินค้าประมูลใช้หมวดหมู่นี้อยู่หรือไม่ เพื่อป้องกันปัญหา Foreign Key และประวัติสินค้าพัง
        var inUse = await Db.AuctionItems.AnyAsync(a => a.CategoryId == id);
        if (inUse)
        {
            // ปิดการใช้งานแทนการลบจริง เพื่อให้สินค้าเดิมยังแสดงชื่อหมวดหมู่ได้ แต่ไม่ให้ลงสินค้าใหม่ในหมวดนี้
            cat.IsActive = false;
            AddAuditLog(admin, "DeactivateCategory", "Category", cat.Id, $"ปิดการใช้งานหมวดหมู่ “{cat.Name}” (มีสินค้าผูกอยู่)");
            await Db.SaveChangesAsync();
            return Ok(new { message = $"หมวดหมู่ “{cat.Name}” มีสินค้าผูกอยู่ ระบบจึงปิดการใช้งานแทนการลบ" });
        }

        // ไม่มีสินค้าอ้างอิงอยู่ สามารถลบออกจากฐานข้อมูลได้ทันที
        Db.Categories.Remove(cat);
        AddAuditLog(admin, "DeleteCategory", "Category", cat.Id, $"ลบหมวดหมู่ “{cat.Name}”");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"ลบหมวดหมู่ “{cat.Name}” แล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : GET /api/admin/disputes/{disputeId}/messages
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟังก์ชัน openDisputeInspector(disputeId)
    //                        (เรียกเมื่อแอดมินกดปุ่มดูรายละเอียดข้อพิพาทในหน้า #view-admin-disputes เพื่อเปิด Modal #actionDialog)
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {disputeId:int} + Cookie "OldThingsAuth" (Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON รายละเอียดข้อพิพาทและข้อความแชททั้งหมดในห้องสนทนาที่ถูกร้องเรียนเรียงตามเวลา
    //                        (ออกแบบด้านความเป็นส่วนตัว: แอดมินอ่านได้เฉพาะแชทที่ผูกกับข้อพิพาทเท่านั้น)
    //                        หรือ 404 NotFound หากไม่พบข้อพิพาทหรือห้องแชท
    [HttpGet("disputes/{disputeId:int}/messages")]
    public async Task<IActionResult> GetDisputeChat(int disputeId)
    {
        // โหลดข้อพิพาทพร้อมห้องสนทนา คู่กรณี และข้อความแชททั้งหมด (จำกัดสิทธิ์เข้าถึงแชทผ่าน disputeId เท่านั้น)
        var dispute = await Db.Disputes
            .Include(d => d.Reporter)
            .Include(d => d.OrderPayment)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.AuctionItem)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Buyer)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Seller)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(d => d.Id == disputeId);

        if (dispute is null || dispute.Conversation is null)
            return NotFound(new { message = "ไม่พบข้อพิพาทนี้ (แอดมินดูได้เฉพาะแชทที่ถูกร้องเรียนเท่านั้น)" });

        // ส่งรายละเอียดข้อพิพาทพร้อมบทสนทนาเรียงจากข้อความเก่าไปใหม่ เพื่อให้แอดมินพิจารณาหลักฐานได้ชัดเจน
        return Ok(new
        {
            dispute.Id,
            dispute.Reason,
            dispute.Details,
            dispute.Status,
            dispute.AdminNote,
            dispute.CreatedAt,
            orderCode = dispute.OrderPayment?.OrderCode,
            itemName = dispute.Conversation.AuctionItem?.Name ?? "",
            buyerName = dispute.Conversation.Buyer?.DisplayName ?? "",
            sellerName = dispute.Conversation.Seller?.ShopName ?? dispute.Conversation.Seller?.DisplayName ?? "",
            reporterName = dispute.Reporter?.DisplayName ?? "",
            messages = dispute.Conversation.Messages.OrderBy(m => m.SentAt).Select(m => new
            {
                m.Id,
                senderName = m.Sender?.DisplayName ?? "",
                // ระบุบทบาทผู้ส่งข้อความในแชทว่าเป็น "ผู้ประมูล" หรือ "ผู้ขาย" เพื่อให้อ่านหลักฐานง่าย
                senderRole = m.SenderId == dispute.Conversation.BuyerId ? "ผู้ประมูล" : "ผู้ขาย",
                m.Body,
                m.SentAt
            })
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/admin/disputes/{disputeId}/update
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟอร์มอัปเดตสถานะข้อพิพาทภายในฟังก์ชัน openDisputeInspector(disputeId)
    //                        (ใน Modal #actionDialog ที่เปิดจากหน้า #view-admin-disputes)
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {disputeId:int} + JSON Body (UpdateDisputeRequest: status, adminNote)
    //                        + Cookie "OldThingsAuth" (Role = Admin)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (ปรับสถานะข้อพิพาท, บันทึกเวลา ResolvedAt เมื่อปิดเคสซึ่งจะปลดล็อกให้ฝ่ายการเงินปล่อยเงินได้,
    //                        และบันทึก AuditLog) หรือ 400 BadRequest (สถานะไม่อยู่ในรายการที่อนุญาต), 401 Unauthorized, 404 NotFound
    [HttpPost("disputes/{disputeId:int}/update")]
    public async Task<IActionResult> UpdateDispute(int disputeId, [FromBody] UpdateDisputeRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var dispute = await Db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId);
        if (dispute is null) return NotFound(new { message = "ไม่พบข้อพิพาท" });

        // ตรวจสอบว่าสถานะที่ส่งมาอยู่ในชุดค่าที่ระบบรองรับเท่านั้น (Open, Investigating, Resolved)
        var allowed = new[] { DisputeStatuses.Open, DisputeStatuses.Investigating, DisputeStatuses.Resolved };
        if (!allowed.Contains(req.Status))
            return BadRequest(new { message = "สถานะข้อพิพาทไม่ถูกต้อง" });

        // อัปเดตสถานะและหมายเหตุของแอดมิน (หากเปลี่ยนเป็น Resolved จะบันทึกเวลาปิดเคส และฝ่ายการเงินจะสามารถปล่อยเงิน Escrow ได้)
        dispute.Status = req.Status;
        dispute.AdminNote = req.AdminNote?.Trim();
        dispute.ResolvedAt = req.Status == DisputeStatuses.Resolved ? DateTime.UtcNow : null;

        // บันทึก Audit Log การอัปเดตสถานะข้อพิพาท
        AddAuditLog(admin, "UpdateDispute", "Dispute", dispute.Id, $"ปรับสถานะข้อพิพาท #{dispute.Id} -> {dispute.Status} ({dispute.AdminNote ?? "-"})");
        await Db.SaveChangesAsync();
        return Ok(new { message = "อัปเดตสถานะข้อพิพาทเรียบร้อยแล้ว" });
    }
}
