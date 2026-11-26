// ============================================================================
// ไฟล์: Controllers/ChatController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend REST API Controller) - จัดการระบบแชทผู้ซื้อ-ผู้ขาย และการแจ้งข้อพิพาท
// หน้าที่หลัก:
//   - ดึงรายการห้องสนทนา (Conversations) ของผู้ใช้ พร้อมข้อความล่าสุด จำนวนข้อความที่ยังไม่อ่าน และสถานะข้อพิพาท
//   - เปิดห้องสนทนาใหม่ (หรือคืนห้องเดิมที่มีอยู่แล้ว) จากหน้าสินค้าประมูล
//   - โหลดประวัติข้อความทั้งหมดในห้องแชท พร้อมอัปเดตเวลาอ่านล่าสุดและล้าง Notification ข้อความ
//   - ส่งข้อความใหม่ในห้องแชท พร้อมสร้าง Notification แจ้งเตือนคู่สนทนาอีกฝ่าย
//   - ส่งเรื่องร้องเรียนข้อพิพาท (Dispute) ผูกกับห้องแชทและออเดอร์ เพื่อให้แอดมินเข้ามาตรวจสอบบทสนทนาได้
// การเชื่อมต่อกับเลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): `wwwroot/js/site.js` เรียกผ่าน `api()` (ใช้ `fetch()` + คุกกี้ `OldThingsAuth`)
//     ในหน้า "กล่องข้อความ" (`view-messages`), หน้าต่างสินค้า (`auctionDialog`), และหน้าต่างแจ้งข้อพิพาท (`actionDialog`)
//   - สิทธิ์การเข้าถึง: อนุญาตเฉพาะบทบาท Buyer และ Seller (`[Authorize(Roles = "Buyer,Seller")]`)
//     (แอดมินจะดูแชทได้เฉพาะเคสที่มีการแจ้งข้อพิพาทผ่าน `AdminController` เท่านั้น)
//   - เลเยอร์ฐานข้อมูล: ใช้ `AppDbContext` (ตาราง Conversations, ChatMessages, OrderPayments, Disputes, Notifications)
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// จำกัดสิทธิ์เฉพาะผู้ประมูล (Buyer) และผู้ขาย (Seller) และกำหนด Route หลักเป็น /api/chat
[Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
[Route("api/chat")]
public class ChatController(AppDbContext db) : ApiBaseController(db)
{
    // DTO สำหรับขอเปิดห้องสนทนากับผู้ขายของสินค้านั้นๆ (ระบุรหัสสินค้า AuctionItemId)
    public record StartChatRequest(int AuctionItemId);
    // DTO สำหรับส่งข้อความแชท (ระบุเนื้อหาข้อความ Body)
    public record SendMessageRequest(string Body);
    // DTO สำหรับแจ้งข้อพิพาทจากห้องแชท (หัวข้อปัญหา, รายละเอียด, และรหัสออเดอร์ถ้ามี)
    public record CreateDisputeRequest(string Reason, string Details, int? OrderPaymentId);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/chat/conversations (เฉพาะ Buyer, Seller)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `loadConversations()` (เมื่อเปิดหน้ากล่องข้อความหรือส่งข้อความ)
    //   และ `pollNotifications()` (รีเฟรชรายการแชทเมื่อมีข้อความใหม่เข้ามาขณะอยู่หน้ากล่องข้อความ)
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON Array ของห้องสนทนา [{
    //   id, auctionItemId, itemName, itemImage, peerName, peerRole,
    //   lastMessage, lastSentAt, unreadCount, orderPaymentId, orderCode, hasOpenDispute, disputeCount
    // }, ...] หรือ HTTP 401
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ดึงห้องสนทนาทั้งหมดที่ผู้ใช้เป็นทั้งฝั่งผู้ซื้อหรือผู้ขาย เรียงตามเวลาอัปเดตล่าสุด
        var conversations = await Db.Conversations
            .Include(c => c.AuctionItem)
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Messages)
            .Include(c => c.Disputes)
            .Where(c => c.BuyerId == user.Id || c.SellerId == user.Id)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        // ดึงรายการออเดอร์ที่เกี่ยวข้องกับสินค้าในห้องแชทเหล่านี้ เพื่อแสดงเลข OrderCode และผูกกับการแจ้งข้อพิพาท
        var itemIds = conversations.Select(c => c.AuctionItemId).Distinct().ToList();
        var orders = await Db.OrderPayments
            .Where(o => itemIds.Contains(o.AuctionItemId) && (o.BuyerId == user.Id || o.SellerId == user.Id))
            .ToListAsync();

        // แปลงแต่ละห้องสนทนาเป็นข้อมูลสรุปสำหรับเรนเดอร์แถบรายชื่อแชทด้านซ้าย
        return Ok(conversations.Select(c =>
        {
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var isBuyer = c.BuyerId == user.Id;
            // เลือกเวลาที่อ่านล่าสุดตามบทบาทของเราในห้องแชทนี้
            var lastRead = isBuyer ? c.BuyerLastReadAt : c.SellerLastReadAt;
            // นับข้อความที่คู่สนทนาส่งมาหลังจากเวลาอ่านล่าสุดของเรา
            var unreadCount = c.Messages.Count(m => m.SenderId != user.Id && (!lastRead.HasValue || m.SentAt > lastRead.Value));
            // ข้อมูลคู่สนทนาอีกฝ่าย (ถ้าเราเป็นผู้ซื้อ คู่สนทนาคือผู้ขาย และในทางกลับกัน)
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/chat/start (เฉพาะ Buyer, Seller)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ปุ่ม `[data-start-chat]` ("แชทกับผู้ขาย") ในหน้าต่างรายละเอียดสินค้า หรือหน้าออเดอร์ของผู้ซื้อ
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth` + JSON Request Body (`StartChatRequest`: { auctionItemId })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { conversationId }, HTTP 400 (ห้ามแชทกับสินค้าตัวเอง), HTTP 404 (ไม่พบสินค้า)
    [HttpPost("start")]
    public async Task<IActionResult> StartConversation([FromBody] StartChatRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าสินค้ามีอยู่จริงและไม่ใช่สินค้าของผู้ใช้เอง
        var item = await Db.AuctionItems.FirstOrDefaultAsync(a => a.Id == req.AuctionItemId);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้า" });
        if (item.SellerId == user.Id)
            return BadRequest(new { message = "ไม่สามารถเปิดแชทกับสินค้าของตัวเองได้" });

        // หากเคยเปิดห้องสนทนาสำหรับสินค้าชิ้นนี้ระหว่างผู้ซื้อและผู้ขายคู่นี้แล้ว ให้คืนห้องเดิมทันที (ไม่สร้างซ้ำ)
        var existing = await Db.Conversations.FirstOrDefaultAsync(
            c => c.AuctionItemId == item.Id && c.BuyerId == user.Id && c.SellerId == item.SellerId);
        if (existing is not null)
            return Ok(new { conversationId = existing.Id });

        // สร้างห้องสนทนาใหม่และบันทึกลงฐานข้อมูล
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/chat/conversations/{id}/messages (เฉพาะ Buyer, Seller)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `openConversation(convId)` (ตอนคลิกเลือกห้องแชท หรือตอนรับข้อความใหม่สดๆ)
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสห้องสนทนา) + คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { id, auctionItemId, itemName, peerName, disputes, messages },
    //   HTTP 401 (ยังไม่ล็อกอิน), HTTP 403 (ไม่ใช่คู่สนทนาในห้องนี้), HTTP 404 (ไม่พบบทสนทนา)
    [HttpGet("conversations/{id:int}/messages")]
    public async Task<IActionResult> GetMessages(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // โหลดห้องสนทนาพร้อมสินค้า คู่สนทนา รายการข้อพิพาท และข้อความทั้งหมด
        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Disputes)
            .Include(c => c.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        // ตรวจสอบสิทธิ์ความเป็นส่วนตัว: เฉพาะผู้ซื้อหรือผู้ขายที่เป็นเจ้าของห้องแชทเท่านั้นจึงจะอ่านได้
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        // อัปเดตเวลาอ่านข้อความล่าสุดของฝั่งที่เปิดดูห้องแชท
        var isBuyer = conv.BuyerId == user.Id;
        var now = DateTime.UtcNow;
        if (isBuyer) conv.BuyerLastReadAt = now;
        else conv.SellerLastReadAt = now;

        // ทำเครื่องหมาย Notification ประเภทข้อความ ("message") ที่อ้างอิงห้องแชทนี้ว่าอ่านแล้วทั้งหมด
        var unreadNotifs = await Db.Notifications
            .Where(n => n.UserId == user.Id && n.Type == "message" && n.ReferenceId == conv.Id && !n.IsRead)
            .ToListAsync();
        foreach (var n in unreadNotifs) n.IsRead = true;

        await Db.SaveChangesAsync();

        var peer = isBuyer ? conv.Seller : conv.Buyer;

        // ส่งข้อมูลหัวแชท, ข้อพิพาทที่เคยแจ้งในห้องนี้, และรายการข้อความเรียงจากเก่าไปใหม่
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/chat/conversations/{id}/messages (เฉพาะ Buyer, Seller)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#chatForm` (submit) ในหน้ากล่องข้อความ
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสห้องสนทนา) + คุกกี้ `OldThingsAuth` + JSON Body (`SendMessageRequest`: { body })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { id, senderId, senderName, isMine, body, sentAt },
    //   HTTP 400 (ข้อความว่าง), HTTP 403 (ไม่ใช่คู่สนทนา), HTTP 404 (ไม่พบบทสนทนา)
    [HttpPost("conversations/{id:int}/messages")]
    public async Task<IActionResult> SendMessage(int id, [FromBody] SendMessageRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าผู้ใช้พิมพ์ข้อความมาจริง (ไม่เป็นช่องว่างล้วน)
        var body = (req.Body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(new { message = "กรุณาพิมพ์ข้อความ" });

        // โหลดห้องสนทนาและตรวจสอบสิทธิ์ความเป็นคู่สนทนา
        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        // สร้างข้อความแชทใหม่และอัปเดตเวลาล่าสุดของห้องสนทนา (เพื่อดันห้องแชทขึ้นบนสุดของรายการ)
        var now = DateTime.UtcNow;
        var msg = new ChatMessage
        {
            ConversationId = conv.Id,
            SenderId = user.Id,
            Body = body,
            SentAt = now
        };
        conv.UpdatedAt = now;

        // อัปเดตเวลาอ่านล่าสุดของฝั่งผู้ส่งเอง (ข้อความที่เราเพิ่งส่งถือว่าอ่านแล้ว)
        var isBuyer = conv.BuyerId == user.Id;
        if (isBuyer) conv.BuyerLastReadAt = now;
        else conv.SellerLastReadAt = now;

        // เตรียมข้อมูลผู้รับแจ้งเตือนและข้อความตัวอย่าง (ตัดความยาวไม่เกิน 120 ตัวอักษร)
        var recipientId = isBuyer ? conv.SellerId : conv.BuyerId;
        var senderLabel = !isBuyer && !string.IsNullOrWhiteSpace(user.ShopName) ? user.ShopName : user.DisplayName;
        var preview = body.Length > 120 ? body[..120] + "…" : body;

        // บันทึกข้อความพร้อมสร้าง Notification แจ้งเตือนคู่สนทนาอีกฝ่าย
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

        // ส่งข้อมูลข้อความที่เพิ่งบันทึกกลับไปยังหน้าบ้าน
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/chat/conversations/{id}/dispute (เฉพาะ Buyer, Seller)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#reportDisputeForm` (เปิดจากปุ่ม `[data-report-dispute]` "แจ้งข้อพิพาท" บนหัวห้องแชท)
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสห้องสนทนา) + คุกกี้ `OldThingsAuth` + JSON Body (`CreateDisputeRequest`: { reason, details, orderPaymentId? })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message, disputeId }, HTTP 400 (กรอกไม่ครบ), HTTP 403 (ไม่ใช่คู่สนทนา), HTTP 404 (ไม่พบบทสนทนา)
    [HttpPost("conversations/{id:int}/dispute")]
    public async Task<IActionResult> ReportDispute(int id, [FromBody] CreateDisputeRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าห้องสนทนามีอยู่จริงและผู้ใช้เป็นคู่สนทนาในห้องนี้
        var conv = await Db.Conversations
            .Include(c => c.AuctionItem)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (conv is null) return NotFound(new { message = "ไม่พบบทสนทนา" });
        if (conv.BuyerId != user.Id && conv.SellerId != user.Id)
            return Forbid();

        // ตรวจสอบว่ากรอกหัวข้อและรายละเอียดข้อพิพาทครบถ้วน
        if (string.IsNullOrWhiteSpace(req.Reason) || string.IsNullOrWhiteSpace(req.Details))
            return BadRequest(new { message = "กรุณาระบุหัวข้อและรายละเอียดข้อพิพาท" });

        // ค้นหาออเดอร์ที่เกี่ยวข้อง (จาก OrderPaymentId ที่ส่งมา หรือจับคู่จากสินค้าและผู้ซื้อ) เพื่อผูกกับข้อพิพาท
        // ซึ่งจะมีผลต่อการระงับการปล่อยเงิน Escrow อัตโนมัติในฝั่งฝ่ายการเงินจนกว่าข้อพิพาทจะถูกปิด (Resolved)
        var linkedOrder = req.OrderPaymentId.HasValue
            ? await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == req.OrderPaymentId.Value && o.AuctionItemId == conv.AuctionItemId)
            : await Db.OrderPayments.FirstOrDefaultAsync(o => o.AuctionItemId == conv.AuctionItemId && o.BuyerId == conv.BuyerId);

        // สร้างเคสข้อพิพาทใหม่สถานะ Open
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

        // ส่ง Notification แจ้งเตือนแอดมินทุกคนที่ยังไม่ถูกระงับ เพื่อให้เข้ามาตรวจบทสนทนาและไกล่เกลี่ย
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
