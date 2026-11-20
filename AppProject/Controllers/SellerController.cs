using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = Roles.Seller)]
[Route("api/seller")]
public class SellerController(AppDbContext db, AuctionLifecycleService lifecycle) : ApiBaseController(db)
{
    public record SellerApplyRequest(string ShopName, string Phone, string BankAccount, string? SellerBio);

    public record CreateListingRequest(
        string Name,
        int CategoryId,
        string Condition,
        string Description,
        string? ImageUrl,
        decimal StartPrice,
        decimal MinIncrement,
        DateTime StartAt,
        DateTime EndAt);

    public record ScheduleMeetupRequest(string Location, DateTime MeetupAt, string? Note);

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        await lifecycle.CloseExpiredAuctionsAsync();
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var settings = await lifecycle.GetSettingsAsync();

        var listings = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Winner)
            .Include(a => a.Bids).ThenInclude(b => b.Bidder)
            .Where(a => a.SellerId == user.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Where(o => o.SellerId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var convMap = await Db.Conversations
            .Where(c => c.SellerId == user.Id)
            .ToDictionaryAsync(c => (c.AuctionItemId, c.BuyerId), c => c.Id);

        var totalGross = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid).Sum(o => o.FinalBuyerAmount);
        var totalFees = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid).Sum(o => o.PlatformFeeAmount);
        var totalReleased = orders.Where(o => o.PayoutStatus == PayoutStatuses.Released).Sum(o => o.SellerNetAmount);
        var pendingPayout = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid && o.PayoutStatus == PayoutStatuses.Pending).Sum(o => o.SellerNetAmount);

        return Ok(new
        {
            seller = MapUserSummary(user),
            globalMinBidIncrement = settings.GlobalMinBidIncrement,
            platformFeePercent = settings.PlatformFeePercent,
            summary = new
            {
                totalListings = listings.Count,
                activeListings = listings.Count(l => l.Status == AuctionStatuses.Approved && l.EndAt > DateTime.UtcNow),
                totalGross,
                totalFees,
                totalReleased,
                pendingPayout
            },
            listings = listings.Select(a => new
            {
                a.Id,
                a.Name,
                a.CategoryId,
                categoryName = a.Category?.Name ?? "",
                a.Condition,
                a.Description,
                a.ImageUrl,
                a.StartPrice,
                a.CurrentPrice,
                a.MinIncrement,
                a.StartAt,
                a.EndAt,
                a.Status,
                a.RejectReason,
                winnerName = a.Winner?.DisplayName,
                bidCount = a.Bids.Count,
                bids = a.Bids
                    .OrderByDescending(b => b.Amount)
                    .ThenByDescending(b => b.CreatedAt)
                    .Select(b => new
                    {
                        b.Id,
                        bidderName = b.Bidder?.DisplayName ?? "นักสะสม",
                        b.Amount,
                        b.CreatedAt
                    }),
                a.CreatedAt
            }),
            orders = orders.Select(o => new
            {
                o.Id,
                o.OrderCode,
                o.AuctionItemId,
                itemName = o.AuctionItem?.Name ?? "",
                categoryName = o.AuctionItem?.Category?.Name ?? "",
                imageUrl = o.AuctionItem?.ImageUrl ?? "",
                o.BuyerId,
                buyerName = o.Buyer?.DisplayName ?? "",
                buyerPhone = o.Buyer?.Phone ?? "",
                o.WinningPrice,
                o.DiscountAmount,
                o.FinalBuyerAmount,
                o.PlatformFeePercent,
                o.PlatformFeeAmount,
                o.SellerNetAmount,
                o.PaymentStatus,
                o.PaidAt,
                o.MeetupStatus,
                o.MeetupLocation,
                o.MeetupAt,
                o.MeetupNote,
                o.DeliveredAt,
                o.ReceivedAt,
                o.PayoutStatus,
                o.PayoutReleasedAt,
                o.FinanceNote,
                conversationId = convMap.GetValueOrDefault((o.AuctionItemId, o.BuyerId), 0),
                o.CreatedAt
            })
        });
    }

    [HttpPost("apply")]
    public async Task<IActionResult> UpdateApplication([FromBody] SellerApplyRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.ShopName) || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.BankAccount))
            return BadRequest(new { message = "กรุณากรอกข้อมูลร้าน เบอร์โทร และเลขบัญชีรับเงินให้ครบถ้วน" });

        user.ShopName = req.ShopName.Trim();
        user.Phone = req.Phone.Trim();
        user.BankAccount = req.BankAccount.Trim();
        user.SellerBio = req.SellerBio?.Trim();
        if (user.SellerStatus != SellerStatuses.Approved)
        {
            user.SellerStatus = SellerStatuses.Pending;
            user.SellerRejectReason = null;
            user.SellerAppliedAt = DateTime.UtcNow;
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "บันทึกข้อมูลใบสมัครผู้ขายเรียบร้อยแล้ว", user = MapUserSummary(user) });
    }

    [HttpPost("listings")]
    public async Task<IActionResult> CreateListing([FromBody] CreateListingRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        if (user.SellerStatus != SellerStatuses.Approved)
            return StatusCode(403, new { message = "บัญชีผู้ขายของคุณยังไม่ได้รับการอนุมัติจากแอดมิน" });

        var settings = await lifecycle.GetSettingsAsync();

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Condition) || string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { message = "กรุณากรอกชื่อสินค้า สภาพสินค้า และรายละเอียดให้ครบถ้วน" });
        if (req.StartPrice <= 0)
            return BadRequest(new { message = "ราคาเริ่มต้นต้องมากกว่า 0 บาท" });
        if (req.MinIncrement < settings.GlobalMinBidIncrement)
            return BadRequest(new { message = $"ขั้นต่ำการเสนอราคาต้องไม่น้อยกว่า ฿{settings.GlobalMinBidIncrement:N0} ตามกติกาฝ่ายการเงิน" });

        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();
        if (endUtc <= startUtc)
            return BadRequest(new { message = "เวลาสิ้นสุดประมูลต้องอยู่หลังเวลาเริ่มประมูล" });
        if (endUtc <= DateTime.UtcNow)
            return BadRequest(new { message = "เวลาสิ้นสุดประมูลต้องอยู่ในอนาคต" });

        var categoryExists = await Db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.IsActive);
        if (!categoryExists)
            return BadRequest(new { message = "หมวดหมู่ที่เลือกไม่ถูกต้อง" });

        var imageUrl = string.IsNullOrWhiteSpace(req.ImageUrl)
            ? "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80"
            : req.ImageUrl.Trim();

        var item = new AuctionItem
        {
            Name = req.Name.Trim(),
            CategoryId = req.CategoryId,
            SellerId = user.Id,
            Condition = req.Condition.Trim(),
            Description = req.Description.Trim(),
            ImageUrl = imageUrl,
            StartPrice = req.StartPrice,
            CurrentPrice = req.StartPrice,
            MinIncrement = req.MinIncrement,
            StartAt = startUtc,
            EndAt = endUtc,
            Status = AuctionStatuses.Pending,
            CreatedAt = DateTime.UtcNow
        };

        Db.AuctionItems.Add(item);
        await Db.SaveChangesAsync();

        return Ok(new { message = "ส่งสินค้าให้แอดมินตรวจสอบแล้ว", itemId = item.Id });
    }

    [HttpPost("orders/{orderId:int}/meetup")]
    public async Task<IActionResult> ScheduleMeetup(int orderId, [FromBody] ScheduleMeetupRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ต้องรอให้ผู้ประมูลชำระเงินก่อนจึงจะกำหนดนัดรับได้" });
        if (string.IsNullOrWhiteSpace(req.Location))
            return BadRequest(new { message = "กรุณาระบุสถานที่นัดรับ" });

        order.MeetupLocation = req.Location.Trim();
        order.MeetupAt = req.MeetupAt.Kind == DateTimeKind.Utc ? req.MeetupAt : req.MeetupAt.ToUniversalTime();
        order.MeetupNote = req.Note?.Trim();
        if (order.MeetupStatus == MeetupStatuses.NotScheduled)
            order.MeetupStatus = MeetupStatuses.Scheduled;

        // Post automatic meetup message in conversation if exists
        var conv = await Db.Conversations.FirstOrDefaultAsync(
            c => c.AuctionItemId == order.AuctionItemId && c.BuyerId == order.BuyerId && c.SellerId == order.SellerId);
        if (conv is not null)
        {
            Db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conv.Id,
                SenderId = user.Id,
                Body = $"[กำหนดนัดรับ] สถานที่: {order.MeetupLocation} | เวลา: {order.MeetupAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}{(string.IsNullOrWhiteSpace(order.MeetupNote) ? "" : $" | หมายเหตุ: {order.MeetupNote}")}",
                SentAt = DateTime.UtcNow
            });
            conv.UpdatedAt = DateTime.UtcNow;
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "บันทึกกำหนดการนัดรับสินค้าแล้ว" });
    }

    [HttpPost("orders/{orderId:int}/deliver")]
    public async Task<IActionResult> ConfirmDelivered(int orderId)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "รายการนี้ยังไม่ได้ชำระเงิน" });
        if (order.MeetupStatus == MeetupStatuses.Completed)
            return BadRequest(new { message = "ผู้ซื้อยืนยันรับสินค้าเรียบร้อยแล้ว" });

        order.MeetupStatus = MeetupStatuses.Delivered;
        order.DeliveredAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ยืนยันส่งมอบสินค้าแล้ว รอผู้ซื้อยืนยันรับของและฝ่ายการเงินปล่อยเงิน" });
    }
}
