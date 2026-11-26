using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
public class AdminController(AppDbContext db) : ApiBaseController(db)
{
    public record ReviewSellerRequest(bool Approve, string? Reason);
    public record ReviewAuctionRequest(bool Approve, string? Reason);
    public record SuspendUserRequest(bool Suspend, string? Reason);
    public record CategoryRequest(string Name, string? Symbol, string? Description, bool IsActive = true);
    public record UpdateDisputeRequest(string Status, string? AdminNote);

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var now = DateTime.UtcNow;

        var pendingSellers = await Db.Users
            .Where(u => u.SellerStatus == SellerStatuses.Pending)
            .OrderByDescending(u => u.SellerAppliedAt)
            .ToListAsync();

        var pendingAuctions = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Where(a => a.Status == AuctionStatuses.Pending)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var liveCount = await Db.AuctionItems.CountAsync(a => a.Status == AuctionStatuses.Approved && a.EndAt > now);

        var users = await Db.Users
            .OrderBy(u => u.Id)
            .ToListAsync();

        var categories = await Db.Categories
            .OrderBy(c => c.Id)
            .ToListAsync();

        var itemCountsByCategory = await Db.AuctionItems
            .GroupBy(a => a.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var disputes = await Db.Disputes
            .Include(d => d.Reporter)
            .Include(d => d.OrderPayment)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.AuctionItem)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Buyer)
            .Include(d => d.Conversation)!.ThenInclude(c => c!.Seller)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var winners = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
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

    [HttpPost("sellers/{userId:int}/review")]
    public async Task<IActionResult> ReviewSeller(int userId, [FromBody] ReviewSellerRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (target is null) return NotFound(new { message = "ไม่พบบัญชีผู้ใช้" });
        if (target.SellerStatus != SellerStatuses.Pending)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอตรวจสอบ" });

        if (req.Approve)
        {
            target.Role = Roles.Seller;
            target.SellerStatus = SellerStatuses.Approved;
            target.SellerRejectReason = null;
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
            AddAuditLog(admin, "ApproveSeller", "User", target.Id, $"อนุมัติผู้ขาย {target.DisplayName} (ร้าน {target.ShopName})");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธใบสมัครผู้ขาย" });
            target.SellerStatus = SellerStatuses.Rejected;
            target.SellerRejectReason = req.Reason.Trim();
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
            AddAuditLog(admin, "RejectSeller", "User", target.Id, $"ปฏิเสธผู้ขาย {target.DisplayName}: {target.SellerRejectReason}");
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = req.Approve ? $"อนุมัติผู้ขาย “{target.DisplayName}” แล้ว" : $"ปฏิเสธใบสมัครผู้ขาย “{target.DisplayName}” แล้ว" });
    }

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
            item.Status = AuctionStatuses.Approved;
            item.RejectReason = null;
            if (item.EndAt <= DateTime.UtcNow)
                item.EndAt = DateTime.UtcNow.AddHours(24);
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
            AddAuditLog(admin, "ApproveAuction", "AuctionItem", item.Id, $"อนุมัติสินค้า “{item.Name}” เริ่ม ฿{item.StartPrice:N0}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธสินค้า" });
            item.Status = AuctionStatuses.Rejected;
            item.RejectReason = req.Reason.Trim();
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
            AddAuditLog(admin, "RejectAuction", "AuctionItem", item.Id, $"ปฏิเสธสินค้า “{item.Name}”: {item.RejectReason}");
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = req.Approve ? $"อนุมัติสินค้า “{item.Name}” เข้าสู่รายการประมูลแล้ว" : $"ปฏิเสธสินค้า “{item.Name}” พร้อมเหตุผลแล้ว" });
    }

    [HttpPost("users/{userId:int}/suspend")]
    public async Task<IActionResult> SuspendUser(int userId, [FromBody] SuspendUserRequest req)
    {
        var me = await GetCurrentActiveUserAsync();
        if (me is null) return Unauthorized();
        if (me.Id == userId)
            return BadRequest(new { message = "ไม่สามารถระงับบัญชีตัวเองได้" });

        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (target is null) return NotFound(new { message = "ไม่พบบัญชีผู้ใช้" });

        if (req.Suspend && string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "กรุณาระบุเหตุผลในการระงับบัญชี" });

        target.IsSuspended = req.Suspend;
        target.SuspendReason = req.Suspend ? req.Reason!.Trim() : null;
        AddAuditLog(me, req.Suspend ? "SuspendUser" : "UnsuspendUser", "User", target.Id,
            req.Suspend ? $"ระงับบัญชี {target.Email}: {target.SuspendReason}" : $"ปลดระงับบัญชี {target.Email}");
        await Db.SaveChangesAsync();

        return Ok(new { message = req.Suspend ? $"ระงับบัญชี {target.DisplayName} แล้ว" : $"ปลดระงับบัญชี {target.DisplayName} แล้ว" });
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "กรุณาระบุชื่อหมวดหมู่" });
        if (await Db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower()))
            return Conflict(new { message = "มีชื่อหมวดหมู่นี้อยู่แล้ว" });

        var cat = new Category
        {
            Name = name,
            Symbol = string.IsNullOrWhiteSpace(req.Symbol) ? "✦" : req.Symbol.Trim(),
            Description = req.Description?.Trim(),
            IsActive = req.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        Db.Categories.Add(cat);
        await Db.SaveChangesAsync();
        AddAuditLog(admin, "CreateCategory", "Category", cat.Id, $"เพิ่มหมวดหมู่ “{cat.Name}”");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"เพิ่มหมวดหมู่ “{cat.Name}” แล้ว", category = cat });
    }

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
        if (await Db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower()))
            return Conflict(new { message = "มีชื่อหมวดหมู่นี้อยู่แล้ว" });

        cat.Name = name;
        cat.Symbol = string.IsNullOrWhiteSpace(req.Symbol) ? cat.Symbol : req.Symbol.Trim();
        cat.Description = req.Description?.Trim();
        cat.IsActive = req.IsActive;
        AddAuditLog(admin, "UpdateCategory", "Category", cat.Id, $"แก้ไขหมวดหมู่ “{cat.Name}” (Active={cat.IsActive})");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"อัปเดตหมวดหมู่ “{cat.Name}” แล้ว" });
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var cat = await Db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound(new { message = "ไม่พบหมวดหมู่" });

        var inUse = await Db.AuctionItems.AnyAsync(a => a.CategoryId == id);
        if (inUse)
        {
            cat.IsActive = false;
            AddAuditLog(admin, "DeactivateCategory", "Category", cat.Id, $"ปิดการใช้งานหมวดหมู่ “{cat.Name}” (มีสินค้าผูกอยู่)");
            await Db.SaveChangesAsync();
            return Ok(new { message = $"หมวดหมู่ “{cat.Name}” มีสินค้าผูกอยู่ ระบบจึงปิดการใช้งานแทนการลบ" });
        }

        Db.Categories.Remove(cat);
        AddAuditLog(admin, "DeleteCategory", "Category", cat.Id, $"ลบหมวดหมู่ “{cat.Name}”");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"ลบหมวดหมู่ “{cat.Name}” แล้ว" });
    }

    [HttpGet("disputes/{disputeId:int}/messages")]
    public async Task<IActionResult> GetDisputeChat(int disputeId)
    {
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
                senderRole = m.SenderId == dispute.Conversation.BuyerId ? "ผู้ประมูล" : "ผู้ขาย",
                m.Body,
                m.SentAt
            })
        });
    }

    [HttpPost("disputes/{disputeId:int}/update")]
    public async Task<IActionResult> UpdateDispute(int disputeId, [FromBody] UpdateDisputeRequest req)
    {
        var admin = await GetCurrentActiveUserAsync();
        if (admin is null) return Unauthorized();

        var dispute = await Db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId);
        if (dispute is null) return NotFound(new { message = "ไม่พบข้อพิพาท" });

        var allowed = new[] { DisputeStatuses.Open, DisputeStatuses.Investigating, DisputeStatuses.Resolved };
        if (!allowed.Contains(req.Status))
            return BadRequest(new { message = "สถานะข้อพิพาทไม่ถูกต้อง" });

        dispute.Status = req.Status;
        dispute.AdminNote = req.AdminNote?.Trim();
        dispute.ResolvedAt = req.Status == DisputeStatuses.Resolved ? DateTime.UtcNow : null;

        AddAuditLog(admin, "UpdateDispute", "Dispute", dispute.Id, $"ปรับสถานะข้อพิพาท #{dispute.Id} -> {dispute.Status} ({dispute.AdminNote ?? "-"})");
        await Db.SaveChangesAsync();
        return Ok(new { message = "อัปเดตสถานะข้อพิพาทเรียบร้อยแล้ว" });
    }
}
