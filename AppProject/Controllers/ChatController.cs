using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
[Route("api/chat")]
public class ChatController(AppDbContext db) : ApiBaseController(db)
{
    public record StartChatRequest(int AuctionItemId);
    public record SendMessageRequest(string Body);
    public record CreateDisputeRequest(string Reason, string Details, int? OrderPaymentId);

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var conversations = await Db.Conversations
            .Include(c => c.AuctionItem)
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Messages)
            .Include(c => c.Disputes)
            .Where(c => c.BuyerId == user.Id || c.SellerId == user.Id)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var itemIds = conversations.Select(c => c.AuctionItemId).Distinct().ToList();
        var orders = await Db.OrderPayments
            .Where(o => itemIds.Contains(o.AuctionItemId) && (o.BuyerId == user.Id || o.SellerId == user.Id))
            .ToListAsync();

        return Ok(conversations.Select(c =>
        {
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var isBuyer = c.BuyerId == user.Id;
            var lastRead = isBuyer ? c.BuyerLastReadAt : c.SellerLastReadAt;
            var unreadCount = c.Messages.Count(m => m.SenderId != user.Id && (!lastRead.HasValue || m.SentAt > lastRead.Value));
            var peer = isBuyer ? c.Seller : c.Buyer;
            var linkedOrder = orders.FirstOrDefault(o => o.AuctionItemId == c.AuctionItemId && o.BuyerId == c.BuyerId && o.SellerId == c.SellerId);
            return new
            {
                c.Id,
                c.AuctionItemId,
                itemName = c.AuctionItem?.Name ?? "",
                itemImage = c.AuctionItem?.ImageUrl ?? "",
                peerName = isBuyer ? (peer?.ShopName ?? peer?.DisplayName ?? "") : (peer?.DisplayName ?? ""),
                peerRole = isBuyer ? "ผู้ขาย" : "ผู้ประมูล",
                lastMessage = lastMsg?.Body ?? "เริ่มต้นการสนทนา",
                lastSentAt = lastMsg?.SentAt ?? c.UpdatedAt,
                unreadCount,
                orderPaymentId = linkedOrder?.Id,
                orderCode = linkedOrder?.OrderCode,
                hasOpenDispute = c.Disputes.Any(d => d.Status != DisputeStatuses.Resolved),
                disputeCount = c.Disputes.Count
            };
        }));
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartConversation([FromBody] StartChatRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var item = await Db.AuctionItems.FirstOrDefaultAsync(a => a.Id == req.AuctionItemId);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้า" });
        if (item.SellerId == user.Id)
            return BadRequest(new { message = "ไม่สามารถเปิดแชทกับสินค้าของตัวเองได้" });

        var existing = await Db.Conversations.FirstOrDefaultAsync(
            c => c.AuctionItemId == item.Id && c.BuyerId == user.Id && c.SellerId == item.SellerId);
        if (existing is not null)
            return Ok(new { conversationId = existing.Id });

        var now = DateTime.UtcNow;
        var conv = new Conversation
        {
            AuctionItemId = item.Id,
            BuyerId = user.Id,
            SellerId = item.SellerId,
            BuyerLastReadAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        Db.Conversations.Add(conv);
        await Db.SaveChangesAsync();

        return Ok(new { conversationId = conv.Id });
    }

    [HttpGet("conversations/{id:int}/messages")]
    public async Task<IActionResult> GetMessages(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Disputes)
            .Include(c => c.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        var isBuyer = conv.BuyerId == user.Id;
        var now = DateTime.UtcNow;
        if (isBuyer) conv.BuyerLastReadAt = now;
        else conv.SellerLastReadAt = now;

        // Mark related unread message notifications as read
        var unreadNotifs = await Db.Notifications
            .Where(n => n.UserId == user.Id && n.Type == "message" && n.ReferenceId == conv.Id && !n.IsRead)
            .ToListAsync();
        foreach (var n in unreadNotifs) n.IsRead = true;

        await Db.SaveChangesAsync();

        var peer = isBuyer ? conv.Seller : conv.Buyer;

        return Ok(new
        {
            conv.Id,
            conv.AuctionItemId,
            itemName = conv.AuctionItem?.Name ?? "",
            peerName = isBuyer ? (peer?.ShopName ?? peer?.DisplayName ?? "") : (peer?.DisplayName ?? ""),
            disputes = conv.Disputes.OrderByDescending(d => d.CreatedAt).Select(d => new
            {
                d.Id,
                d.Reason,
                d.Status,
                d.AdminNote,
                d.CreatedAt
            }),
            messages = conv.Messages.OrderBy(m => m.SentAt).Select(m => new
            {
                m.Id,
                m.SenderId,
                senderName = m.Sender?.DisplayName ?? "",
                isMine = m.SenderId == user.Id,
                m.Body,
                m.SentAt
            })
        });
    }

    [HttpPost("conversations/{id:int}/messages")]
    public async Task<IActionResult> SendMessage(int id, [FromBody] SendMessageRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var body = (req.Body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(new { message = "กรุณาพิมพ์ข้อความ" });

        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        var now = DateTime.UtcNow;
        var msg = new ChatMessage
        {
            ConversationId = conv.Id,
            SenderId = user.Id,
            Body = body,
            SentAt = now
        };
        conv.UpdatedAt = now;

        var isBuyer = conv.BuyerId == user.Id;
        if (isBuyer) conv.BuyerLastReadAt = now;
        else conv.SellerLastReadAt = now;

        var recipientId = isBuyer ? conv.SellerId : conv.BuyerId;
        var senderLabel = !isBuyer && !string.IsNullOrWhiteSpace(user.ShopName) ? user.ShopName : user.DisplayName;
        var preview = body.Length > 120 ? body[..120] + "…" : body;

        Db.ChatMessages.Add(msg);
        Db.Notifications.Add(new Notification
        {
            UserId = recipientId,
            Type = "message",
            Title = $"ข้อความใหม่จาก {senderLabel}",
            Body = $"{(conv.AuctionItem is not null ? $"[{conv.AuctionItem.Name}] " : "")}{preview}",
            TargetView = "messages",
            ReferenceId = conv.Id,
            IsRead = false,
            CreatedAt = now
        });

        await Db.SaveChangesAsync();

        return Ok(new
        {
            msg.Id,
            msg.SenderId,
            senderName = user.DisplayName,
            isMine = true,
            msg.Body,
            msg.SentAt
        });
    }

    [HttpPost("conversations/{id:int}/dispute")]
    public async Task<IActionResult> ReportDispute(int id, [FromBody] CreateDisputeRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        if (string.IsNullOrWhiteSpace(req.Reason) || string.IsNullOrWhiteSpace(req.Details))
            return BadRequest(new { message = "กรุณาระบุหัวข้อและรายละเอียดข้อพิพาท" });

        var linkedOrder = req.OrderPaymentId.HasValue
            ? await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == req.OrderPaymentId.Value && o.AuctionItemId == conv.AuctionItemId)
            : await Db.OrderPayments.FirstOrDefaultAsync(o => o.AuctionItemId == conv.AuctionItemId && o.BuyerId == conv.BuyerId);

        var now = DateTime.UtcNow;
        var dispute = new Dispute
        {
            ConversationId = conv.Id,
            OrderPaymentId = linkedOrder?.Id,
            ReporterId = user.Id,
            Reason = req.Reason.Trim(),
            Details = req.Details.Trim(),
            Status = DisputeStatuses.Open,
            CreatedAt = now
        };

        Db.Disputes.Add(dispute);

        // Notify all Admins
        var adminIds = await Db.Users
            .Where(u => u.Role == Roles.Admin && !u.IsSuspended)
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var adminId in adminIds)
        {
            Db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Type = "dispute",
                Title = "มีข้อพิพาทใหม่รอตรวจสอบ",
                Body = $"{user.DisplayName} แจ้งเรื่อง: {dispute.Reason} ({conv.AuctionItem?.Name ?? ""})",
                TargetView = "admin-disputes",
                IsRead = false,
                CreatedAt = now
            });
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "ส่งเรื่องร้องเรียนข้อพิพาทให้แอดมินแล้ว แอดมินสามารถตรวจสอบบทสนทนาของเคสนี้ได้", disputeId = dispute.Id });
    }
}
