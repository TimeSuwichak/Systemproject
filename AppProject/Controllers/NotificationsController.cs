// ============================================================================
// ไฟล์: Controllers/NotificationsController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend REST API Controller) - จัดการระบบแจ้งเตือนและตัวนับข้อความยังไม่อ่าน
// หน้าที่หลัก:
//   - สรุปการแจ้งเตือนทั้งหมดแบบรวมศูนย์ (Summary) สำหรับผู้ใช้ที่ล็อกอินอยู่ (Polling ทุก 5 วินาที)
//   - นับจำนวนข้อความแชทที่ยังไม่อ่าน + ส่งข้อความล่าสุดเพื่อแสดงป๊อปอัปแจ้งเตือน (Toast) แบบเรียลไทม์
//   - สร้างรายการแจ้งเตือนภารกิจตามบทบาท (Role-specific Alerts) ให้ Admin, Seller และ Finance
//   - ทำเครื่องหมายว่าอ่านแล้วรายรายการ (Mark Read) หรืออ่านทั้งหมดพร้อมกัน (Mark All Read)
// การเชื่อมต่อกับเลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): `wwwroot/js/site.js` เรียกผ่าน `api()` (ใช้ `fetch()` + คุกกี้ `OldThingsAuth`)
//     โดยเฉพาะฟังก์ชัน `pollNotifications()` ที่ดึงข้อมูลอัตโนมัติทุก 5 วินาทีเพื่ออัปเดตกระดิ่งแจ้งเตือนและ Badge แชท
//   - เลเยอร์ฐานข้อมูล/บริการ: ใช้ `AppDbContext` และเมธอด `AuctionLifecycleService.CanReleaseEscrow()`
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// บังคับต้องเข้าสู่ระบบก่อนใช้งานทุก Endpoint ใน Controller นี้ และกำหนด Route หลักเป็น /api/notifications
[Authorize]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ApiBaseController(db)
{
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/notifications/summary (ต้องล็อกอิน: [Authorize])
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `pollNotifications()` (รันอัตโนมัติทุก 5 วินาที, ตอนกดกระดิ่ง `#notifBellBtn`, และหลังอ่านแชท/แจ้งเตือน)
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON {
    //   role, totalCount, unreadMessagesCount, unreadNotifCount,
    //   latestMessageId, latestIncomingMessage, notifications (สูงสุด 20 รายการ), roleAlerts
    // } หรือ HTTP 401 (ยังไม่ล็อกอิน)
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        // ตรวจสอบผู้ใช้ปัจจุบันที่ล็อกอินอยู่และยังไม่ถูกระงับ
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตัวแปรสำหรับเก็บจำนวนข้อความแชทที่ยังไม่อ่าน และข้อความล่าสุดที่เข้ามา
        var unreadMessagesCount = 0;
        object? latestIncomingMessage = null;
        var latestMessageId = 0;

        // คำนวณข้อความแชทยังไม่อ่านเฉพาะบทบาท Buyer และ Seller ที่มีระบบแชท
        if (user.Role is Roles.Buyer or Roles.Seller)
        {
            // โหลดห้องแชททั้งหมดที่ผู้ใช้เป็นคู่สนทนา พร้อมข้อมูลสินค้าและข้อความทั้งหมด
            var myConvs = await Db.Conversations
                .Include(c => c.AuctionItem)
                .Include(c => c.Messages).ThenInclude(m => m.Sender)
                .Where(c => c.BuyerId == user.Id || c.SellerId == user.Id)
                .ToListAsync();

            // กรองเฉพาะข้อความที่ส่งโดยอีกฝ่าย และเวลาส่งใหม่กว่าเวลาอ่านล่าสุด (LastReadAt) ของผู้ใช้
            var unreadMsgs = myConvs
                .SelectMany(c =>
                {
                    var lastRead = c.BuyerId == user.Id ? c.BuyerLastReadAt : c.SellerLastReadAt;
                    return c.Messages
                        .Where(m => m.SenderId != user.Id && (!lastRead.HasValue || m.SentAt > lastRead.Value))
                        .Select(m => new { Conv = c, Msg = m });
                })
                .OrderByDescending(x => x.Msg.SentAt)
                .ToList();

            unreadMessagesCount = unreadMsgs.Count;
            // ดึงข้อความล่าสุด 1 รายการเพื่อส่งให้หน้าบ้านเทียบ `latestMessageId` และเด้ง Toast แจ้งเตือนสด
            var latest = unreadMsgs.FirstOrDefault();
            if (latest is not null)
            {
                latestMessageId = latest.Msg.Id;
                latestIncomingMessage = new
                {
                    messageId = latest.Msg.Id,
                    conversationId = latest.Conv.Id,
                    senderName = latest.Msg.Sender?.DisplayName ?? "คู่สนทนา",
                    itemName = latest.Conv.AuctionItem?.Name ?? "",
                    body = latest.Msg.Body,
                    sentAt = latest.Msg.SentAt
                };
            }
        }

        // ดึงรายการแจ้งเตือน (Notification) ล่าสุด 20 รายการของผู้ใช้นี้จากฐานข้อมูล
        var notifications = await Db.Notifications
            .Where(n => n.UserId == user.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new
            {
                n.Id,
                n.Type,
                n.Title,
                n.Body,
                n.TargetView,
                n.ReferenceId,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        // นับจำนวนแจ้งเตือนที่ยังไม่ได้เปิดอ่าน
        var unreadNotifCount = notifications.Count(n => !n.IsRead);

        // สร้างรายการแจ้งเตือนงานค้างตามบทบาท (Role-specific operational alerts) แบบคำนวณสดจากฐานข้อมูล
        var roleAlerts = new List<object>();
        if (user.Role == Roles.Admin)
        {
            // แอดมิน: แจ้งจำนวนใบสมัครผู้ขายรออนุมัติ, สินค้ารออนุมัติ, และข้อพิพาทที่ยังไม่ปิดเคส
            var pendingSellers = await Db.Users.CountAsync(u => u.SellerStatus == SellerStatuses.Pending);
            var pendingAuctions = await Db.AuctionItems.CountAsync(a => a.Status == AuctionStatuses.Pending);
            var openDisputes = await Db.Disputes.CountAsync(d => d.Status != DisputeStatuses.Resolved);
            if (pendingSellers > 0) roleAlerts.Add(new { view = "admin-approvals", title = $"ใบสมัครผู้ขายรออนุมัติ {pendingSellers} รายการ", body = "คลิกเพื่อตรวจสอบและอนุมัติ" });
            if (pendingAuctions > 0) roleAlerts.Add(new { view = "admin-approvals", title = $"สินค้ารออนุมัติ {pendingAuctions} รายการ", body = "คลิกเพื่อตรวจรายละเอียดสินค้า" });
            if (openDisputes > 0) roleAlerts.Add(new { view = "admin-disputes", title = $"ข้อพิพาทที่เปิดอยู่ {openDisputes} เคส", body = "คลิกเพื่อตรวจสอบแชทที่ถูกร้องเรียน" });
        }
        else if (user.Role == Roles.Seller)
        {
            // ผู้ขาย: แจ้งจำนวนออเดอร์ที่ผู้ซื้อเลือก "จ่ายตรงผู้ขาย" และกำลังรอผู้ขายกดยืนยันรับเงิน
            var pendingDirect = await Db.OrderPayments.CountAsync(o =>
                o.SellerId == user.Id
                && o.PaymentMethod == PaymentMethods.DirectToSeller
                && o.PaymentStatus == PaymentStatuses.PendingVerification);
            if (pendingDirect > 0)
                roleAlerts.Add(new { view = "seller-orders", title = $"รายการแจ้งจ่ายตรงรอยืนยัน {pendingDirect} รายการ", body = "ผู้ซื้อแจ้งชำระเงินตรงแล้ว คลิกเพื่อตรวจสอบและกดยืนยัน" });
        }
        else if (user.Role == Roles.Finance)
        {
            // ฝ่ายการเงิน (1): นับจำนวนสลิป QR ระบบกลาง (Escrow) ที่รอฝ่ายการเงินตรวจสอบ
            var pendingEscrowSlips = await Db.OrderPayments.CountAsync(o =>
                o.PaymentMethod == PaymentMethods.PlatformEscrow
                && o.PaymentStatus == PaymentStatuses.PendingVerification);
            if (pendingEscrowSlips > 0)
                roleAlerts.Add(new { view = "finance-payments", title = $"มีสลิป QR ระบบกลางรอตรวจ {pendingEscrowSlips} รายการ", body = "คลิกเพื่อตรวจสลิปและยืนยันรับชำระเงิน" });

            // ฝ่ายการเงิน (2): รวบรวม OrderId ที่มีข้อพิพาทเปิดอยู่ เพื่อห้ามปล่อยเงินให้ผู้ขาย
            var openDisputeOrderIds = (await Db.Disputes
                .Where(d => d.OrderPaymentId != null && d.Status != DisputeStatuses.Resolved)
                .Select(d => d.OrderPaymentId!.Value)
                .Distinct()
                .ToListAsync()).ToHashSet();

            // ดึงออเดอร์ Escrow ที่ชำระเงินแล้วแต่ยังรอปล่อยเงิน (PayoutStatus == Pending)
            var escrowPending = await Db.OrderPayments
                .Where(o => o.PaymentMethod == PaymentMethods.PlatformEscrow
                    && o.PaymentStatus == PaymentStatuses.Paid
                    && o.PayoutStatus == PayoutStatuses.Pending)
                .ToListAsync();

            // ตรวจเงื่อนไขการปล่อยเงินตามกติกา Escrow (ผู้ซื้อยืนยันรับของแล้วหรือพ้นกำหนด และไม่มีข้อพิพาทค้าง)
            var nowUtc = DateTime.UtcNow;
            var readyToRelease = escrowPending.Count(o =>
                AppProject.Services.AuctionLifecycleService.CanReleaseEscrow(o, openDisputeOrderIds.Contains(o.Id), nowUtc));
            if (readyToRelease > 0)
                roleAlerts.Add(new { view = "finance-payments", title = $"พร้อมปล่อยเงินให้ผู้ขาย {readyToRelease} รายการ", body = "ผู้ซื้อยืนยันรับของแล้ว คลิกเพื่อปล่อยเงิน" });
        }

        // คำนวณตัวเลขรวมบนไอคอนกระดิ่ง (Badge) ด้านบนของหน้าเว็บ
        var totalBadgeCount = Math.Max(unreadNotifCount, unreadMessagesCount) + (user.Role is Roles.Admin or Roles.Finance && unreadNotifCount == 0 ? roleAlerts.Count : 0);

        return Ok(new
        {
            role = user.Role,
            totalCount = totalBadgeCount,
            unreadMessagesCount,
            unreadNotifCount,
            latestMessageId,
            latestIncomingMessage,
            notifications,
            roleAlerts
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/notifications/{id}/read (ต้องล็อกอิน: [Authorize])
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> Event Listener คลิกที่รายการแจ้งเตือน `[data-notif-view]` ใน Dropdown กระดิ่ง
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัส Notification) + คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { ok: true }, HTTP 401 (ยังไม่ล็อกอิน), HTTP 404 (ไม่พบหรือไม่ใช่ของตนเอง)
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ค้นหาแจ้งเตือนโดยตรวจทั้ง Id และ UserId เพื่อป้องกันไม่ให้ผู้อื่นสั่งอ่านแจ้งเตือนข้ามบัญชี
        var notif = await Db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == user.Id);
        if (notif is null) return NotFound();

        // เปลี่ยนสถานะเป็นอ่านแล้วและบันทึกลงฐานข้อมูล
        notif.IsRead = true;
        await Db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/notifications/read-all (ต้องล็อกอิน: [Authorize])
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ปุ่ม `#markAllReadBtn` ("อ่านทั้งหมด") ใน Dropdown กระดิ่ง
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message } หรือ HTTP 401 (ยังไม่ล็อกอิน)
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ทำเครื่องหมายแจ้งเตือน (Notification) ทุกอันที่ยังไม่อ่านของผู้ใช้นี้เป็นอ่านแล้วทั้งหมด
        var unreadNotifs = await Db.Notifications
            .Where(n => n.UserId == user.Id && !n.IsRead)
            .ToListAsync();
        foreach (var n in unreadNotifs) n.IsRead = true;

        // อัปเดตเวลาอ่านแชทล่าสุด (LastReadAt) ทุกห้องสนทนาของผู้ใช้ เพื่อล้างตัวนับแชทยังไม่อ่านพร้อมกัน
        var now = DateTime.UtcNow;
        var myConvs = await Db.Conversations
            .Where(c => c.BuyerId == user.Id || c.SellerId == user.Id)
            .ToListAsync();
        foreach (var c in myConvs)
        {
            if (c.BuyerId == user.Id) c.BuyerLastReadAt = now;
            if (c.SellerId == user.Id) c.SellerLastReadAt = now;
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "ทำเครื่องหมายว่าอ่านทั้งหมดแล้ว" });
    }
}
