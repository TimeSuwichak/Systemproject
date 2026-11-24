using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ApiBaseController(db)
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var unreadMessagesCount = 0;
        object? latestIncomingMessage = null;
        var latestMessageId = 0;

        if (user.Role is Roles.Buyer or Roles.Seller)
        {
            var myConvs = await Db.Conversations
                .Include(c => c.AuctionItem)
                .Include(c => c.Messages).ThenInclude(m => m.Sender)
                .Where(c => c.BuyerId == user.Id || c.SellerId == user.Id)
                .ToListAsync();

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

        var unreadNotifCount = notifications.Count(n => !n.IsRead);

        // Role-specific operational alerts
        var roleAlerts = new List<object>();
        if (user.Role == Roles.Admin)
        {
            var pendingSellers = await Db.Users.CountAsync(u => u.SellerStatus == SellerStatuses.Pending);
            var pendingAuctions = await Db.AuctionItems.CountAsync(a => a.Status == AuctionStatuses.Pending);
            var openDisputes = await Db.Disputes.CountAsync(d => d.Status != DisputeStatuses.Resolved);
            if (pendingSellers > 0) roleAlerts.Add(new { view = "admin-approvals", title = $"ใบสมัครผู้ขายรออนุมัติ {pendingSellers} รายการ", body = "คลิกเพื่อตรวจสอบและอนุมัติ" });
            if (pendingAuctions > 0) roleAlerts.Add(new { view = "admin-approvals", title = $"สินค้ารออนุมัติ {pendingAuctions} รายการ", body = "คลิกเพื่อตรวจรายละเอียดสินค้า" });
            if (openDisputes > 0) roleAlerts.Add(new { view = "admin-disputes", title = $"ข้อพิพาทที่เปิดอยู่ {openDisputes} เคส", body = "คลิกเพื่อตรวจสอบแชทที่ถูกร้องเรียน" });
        }
        else if (user.Role == Roles.Seller)
        {
            var pendingDirect = await Db.OrderPayments.CountAsync(o =>
                o.SellerId == user.Id
                && o.PaymentMethod == PaymentMethods.DirectToSeller
                && o.PaymentStatus == PaymentStatuses.PendingVerification);
            if (pendingDirect > 0)
                roleAlerts.Add(new { view = "seller-orders", title = $"รายการแจ้งจ่ายตรงรอยืนยัน {pendingDirect} รายการ", body = "ผู้ซื้อแจ้งชำระเงินตรงแล้ว คลิกเพื่อตรวจสอบและกดยืนยัน" });
        }
        else if (user.Role == Roles.Finance)
        {
            var pendingEscrowSlips = await Db.OrderPayments.CountAsync(o =>
                o.PaymentMethod == PaymentMethods.PlatformEscrow
                && o.PaymentStatus == PaymentStatuses.PendingVerification);
            if (pendingEscrowSlips > 0)
                roleAlerts.Add(new { view = "finance-payments", title = $"มีสลิป QR ระบบกลางรอตรวจ {pendingEscrowSlips} รายการ", body = "คลิกเพื่อตรวจสลิปและยืนยันรับชำระเงิน" });

            var readyToRelease = await Db.OrderPayments.CountAsync(o =>
                o.PaymentMethod == PaymentMethods.PlatformEscrow
                && o.PaymentStatus == PaymentStatuses.Paid
                && o.PayoutStatus == PayoutStatuses.Pending
                && (o.MeetupStatus == MeetupStatuses.Completed || o.MeetupStatus == MeetupStatuses.Delivered));
            if (readyToRelease > 0)
                roleAlerts.Add(new { view = "finance-payments", title = $"พร้อมปล่อยเงินให้ผู้ขาย {readyToRelease} รายการ", body = "ยืนยันส่งมอบ/รับของแล้ว คลิกเพื่อตรวจสอบ" });
        }

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

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var notif = await Db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == user.Id);
        if (notif is null) return NotFound();

        notif.IsRead = true;
        await Db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var unreadNotifs = await Db.Notifications
            .Where(n => n.UserId == user.Id && !n.IsRead)
            .ToListAsync();
        foreach (var n in unreadNotifs) n.IsRead = true;

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
