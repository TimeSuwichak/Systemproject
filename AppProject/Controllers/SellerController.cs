using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = Roles.Seller)]
[Route("api/seller")]
public class SellerController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public record SellerApplyRequest(string ShopName, string Phone, string BankAccount, string? SellerBio);

    public record ListingPayload(
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
    public record VerifyDirectPaymentRequest(bool Approve, string? Reason);

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
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
        var totalFees = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid && o.PaymentMethod == PaymentMethods.PlatformEscrow).Sum(o => o.PlatformFeeAmount);
        var totalReleased = orders.Where(o => o.PayoutStatus == PayoutStatuses.Released || o.PayoutStatus == PayoutStatuses.DirectSettled).Sum(o => o.SellerNetAmount);
        var pendingPayout = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid && o.PayoutStatus == PayoutStatuses.Pending && o.PaymentMethod == PaymentMethods.PlatformEscrow).Sum(o => o.SellerNetAmount);

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
                canEditOrCancel = a.Status is AuctionStatuses.Pending or AuctionStatuses.Rejected,
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
                o.PaymentMethod,
                o.PaymentStatus,
                o.PaymentSlipUrl,
                o.PaymentNote,
                o.PaymentSubmittedAt,
                o.PaymentDueAt,
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
                canVerifyDirectPayment = o.PaymentMethod == PaymentMethods.DirectToSeller && o.PaymentStatus == PaymentStatuses.PendingVerification,
                conversationId = convMap.GetValueOrDefault((o.AuctionItemId, o.BuyerId), 0),
                o.CreatedAt
            })
        });
    }

    // Consolidated single apply-seller endpoint for BOTH Buyer and Seller
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
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
        return Ok(new { message = "ส่งใบสมัครผู้ขายให้แอดมินตรวจสอบแล้ว", user = MapUserSummary(user) });
    }

    [HttpPost("upload-image")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadProductImage([FromForm] IFormFile? file)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();
        if (user.SellerStatus != SellerStatuses.Approved)
            return StatusCode(403, new { message = "บัญชีผู้ขายยังไม่ได้รับการอนุมัติ" });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "กรุณาเลือกไฟล์รูปภาพ" });
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "ไฟล์รูปภาพต้องมีขนาดไม่เกิน 5 MB" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return BadRequest(new { message = "รองรับเฉพาะไฟล์ .jpg, .jpeg, .png หรือ .webp เท่านั้น" });

        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var productsDir = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(productsDir);

        var fileName = $"item-{user.Id}-{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(productsDir, fileName);
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs);
        }

        return Ok(new { imageUrl = $"/uploads/products/{fileName}", message = "อัปโหลดรูปสินค้าเรียบร้อย" });
    }

    [HttpPost("listings")]
    public async Task<IActionResult> CreateListing([FromBody] ListingPayload req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        if (user.SellerStatus != SellerStatuses.Approved)
            return StatusCode(403, new { message = "บัญชีผู้ขายของคุณยังไม่ได้รับการอนุมัติจากแอดมิน" });

        var validationError = await ValidateListingPayloadAsync(req);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();
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
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };

        Db.AuctionItems.Add(item);
        await Db.SaveChangesAsync();

        return Ok(new { message = "ส่งสินค้าให้แอดมินตรวจสอบแล้ว", itemId = item.Id });
    }

    [HttpPut("listings/{id:int}")]
    public async Task<IActionResult> UpdateListing(int id, [FromBody] ListingPayload req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var item = await Db.AuctionItems.FirstOrDefaultAsync(a => a.Id == id && a.SellerId == user.Id);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้าของคุณ" });

        if (item.Status is not (AuctionStatuses.Pending or AuctionStatuses.Rejected))
            return BadRequest(new { message = "แก้ไขได้เฉพาะสินค้าที่ยังรออนุมัติหรือถูกปฏิเสธเท่านั้น" });

        var validationError = await ValidateListingPayloadAsync(req);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();

        item.Name = req.Name.Trim();
        item.CategoryId = req.CategoryId;
        item.Condition = req.Condition.Trim();
        item.Description = req.Description.Trim();
        if (!string.IsNullOrWhiteSpace(req.ImageUrl))
            item.ImageUrl = req.ImageUrl.Trim();
        item.StartPrice = req.StartPrice;
        item.CurrentPrice = req.StartPrice;
        item.MinIncrement = req.MinIncrement;
        item.StartAt = startUtc;
        item.EndAt = endUtc;
        item.Status = AuctionStatuses.Pending;
        item.RejectReason = null;
        item.Version++;

        await Db.SaveChangesAsync();
        return Ok(new { message = "บันทึกการแก้ไขสินค้าและส่งให้แอดมินตรวจสอบอีกครั้งแล้ว" });
    }

    [HttpDelete("listings/{id:int}")]
    public async Task<IActionResult> DeleteListing(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var item = await Db.AuctionItems
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id && a.SellerId == user.Id);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้าของคุณ" });

        if (item.Status is not (AuctionStatuses.Pending or AuctionStatuses.Rejected))
            return BadRequest(new { message = "ยกเลิกได้เฉพาะสินค้าที่ยังรออนุมัติหรือถูกปฏิเสธเท่านั้น" });
        if (item.Bids.Count > 0)
            return BadRequest(new { message = "ไม่สามารถยกเลิกสินค้าที่มีการเสนอราคาแล้ว" });

        Db.AuctionItems.Remove(item);
        await Db.SaveChangesAsync();
        return Ok(new { message = $"ยกเลิกรายการสินค้า “{item.Name}” แล้ว" });
    }

    [HttpPost("orders/{orderId:int}/meetup")]
    public async Task<IActionResult> ScheduleMeetup(int orderId, [FromBody] ScheduleMeetupRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ต้องรอให้ยืนยันการชำระเงินก่อนจึงจะกำหนดนัดรับได้" });
        if (string.IsNullOrWhiteSpace(req.Location))
            return BadRequest(new { message = "กรุณาระบุสถานที่นัดรับ" });

        order.MeetupLocation = req.Location.Trim();
        order.MeetupAt = req.MeetupAt.Kind == DateTimeKind.Utc ? req.MeetupAt : req.MeetupAt.ToUniversalTime();
        order.MeetupNote = req.Note?.Trim();
        if (order.MeetupStatus == MeetupStatuses.NotScheduled)
            order.MeetupStatus = MeetupStatuses.Scheduled;

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

        return Ok(new { message = "ยืนยันส่งมอบสินค้าแล้ว รอผู้ซื้อยืนยันรับของเพื่อให้ฝ่ายการเงินปล่อยเงิน" });
    }

    [HttpPost("orders/{orderId:int}/verify-payment")]
    public async Task<IActionResult> VerifyDirectPayment(int orderId, [FromBody] VerifyDirectPaymentRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });

        if (order.PaymentMethod != PaymentMethods.DirectToSeller)
            return BadRequest(new { message = "รายการนี้ชำระผ่าน QR ระบบกลาง ต้องให้ฝ่ายการเงินเป็นผู้ตรวจสอบ" });
        if (order.PaymentStatus != PaymentStatuses.PendingVerification)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอผู้ขายยืนยันการชำระเงิน" });

        var now = DateTime.UtcNow;
        if (req.Approve)
        {
            order.PaymentStatus = PaymentStatuses.Paid;
            order.PaidAt = now;
            order.PayoutStatus = PayoutStatuses.DirectSettled;
            order.PayoutReleasedAt = now;
            order.PlatformFeePercent = 0;
            order.PlatformFeeAmount = 0;
            order.SellerNetAmount = order.FinalBuyerAmount;
            order.PaymentRejectReason = null;
            order.FinanceNote = "ผู้ขายยืนยันได้รับเงินชำระตรงจากผู้ซื้อแล้ว";

            Db.Notifications.Add(new Notification
            {
                UserId = order.BuyerId,
                Type = "payment_verified",
                Title = $"ผู้ขายยืนยันรับเงินแล้ว ({order.OrderCode})",
                Body = $"สินค้า “{order.AuctionItem?.Name}” ยืนยันชำระเงินตรงเรียบร้อย รอผู้ขายกำหนดนัดรับได้เลย",
                TargetView = "payments",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });

            await Db.SaveChangesAsync();
            await lifecycle.RecalculateUnpaidOrdersAsync(order.BuyerId);
            return Ok(new { message = $"ยืนยันได้รับเงินตรงสำหรับ {order.OrderCode} แล้ว" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธการชำระเงิน" });

            order.PaymentStatus = PaymentStatuses.Unpaid;
            order.PaymentRejectReason = req.Reason.Trim();

            Db.Notifications.Add(new Notification
            {
                UserId = order.BuyerId,
                Type = "payment_rejected",
                Title = $"ผู้ขายยังยืนยันยอดชำระไม่ได้ ({order.OrderCode})",
                Body = $"เหตุผล: {order.PaymentRejectReason} — กรุณาตรวจสอบและแจ้งชำระเงินใหม่อีกครั้ง",
                TargetView = "payments",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });

            await Db.SaveChangesAsync();
            return Ok(new { message = $"ปฏิเสธการแจ้งชำระเงิน {order.OrderCode} แล้ว" });
        }
    }

    private async Task<string?> ValidateListingPayloadAsync(ListingPayload req)
    {
        var settings = await lifecycle.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Condition) || string.IsNullOrWhiteSpace(req.Description))
            return "กรุณากรอกชื่อสินค้า สภาพสินค้า และรายละเอียดให้ครบถ้วน";
        if (req.StartPrice <= 0)
            return "ราคาเริ่มต้นต้องมากกว่า 0 บาท";
        if (req.MinIncrement < settings.GlobalMinBidIncrement)
            return $"ขั้นต่ำการเสนอราคาต้องไม่น้อยกว่า ฿{settings.GlobalMinBidIncrement:N0} ตามกติกาฝ่ายการเงิน";

        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();
        if (endUtc <= startUtc)
            return "เวลาสิ้นสุดประมูลต้องอยู่หลังเวลาเริ่มประมูล";
        if (endUtc <= DateTime.UtcNow)
            return "เวลาสิ้นสุดประมูลต้องอยู่ในอนาคต";

        var categoryExists = await Db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.IsActive);
        if (!categoryExists)
            return "หมวดหมู่ที่เลือกไม่ถูกต้อง";

        return null;
    }
}
