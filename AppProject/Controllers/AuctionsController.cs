using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Route("api")]
public class AuctionsController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    private static readonly string[] AllowedSlipExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly TimeSpan AntiSnipingWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AntiSnipingExtension = TimeSpan.FromMinutes(2);

    public record PlaceBidRequest(decimal Amount);

    [HttpGet("auctions")]
    public async Task<IActionResult> GetAuctions([FromQuery] int? categoryId, [FromQuery] string? search, [FromQuery] string? sort)
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        var now = DateTime.UtcNow;

        var categories = await Db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var allLive = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatuses.Approved && a.EndAt > now)
            .ToListAsync();

        var categoryCounts = allLive.GroupBy(a => a.CategoryId).ToDictionary(g => g.Key, g => g.Count());

        IEnumerable<AuctionItem> filtered = allLive;
        if (categoryId.HasValue && categoryId.Value > 0)
            filtered = filtered.Where(a => a.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(a =>
                a.Name.ToLowerInvariant().Contains(term) ||
                a.Condition.ToLowerInvariant().Contains(term) ||
                a.Description.ToLowerInvariant().Contains(term) ||
                (a.Category?.Name.ToLowerInvariant().Contains(term) ?? false));
        }

        filtered = sort == "newest"
            ? filtered.OrderByDescending(a => a.CreatedAt)
            : filtered.OrderBy(a => a.EndAt);

        var savedIds = user is null
            ? new HashSet<int>()
            : (await Db.SavedItems.Where(s => s.UserId == user.Id).Select(s => s.AuctionItemId).ToListAsync()).ToHashSet();

        // Eligible for same-category discount only when buyer already has a PAID order in that category
        var buyerPaidCategoryIds = user is null
            ? new HashSet<int>()
            : (await Db.OrderPayments
                .Include(o => o.AuctionItem)
                .Where(o => o.BuyerId == user.Id && o.PaymentStatus == PaymentStatuses.Paid && o.AuctionItem != null)
                .Select(o => o.AuctionItem!.CategoryId)
                .Distinct()
                .ToListAsync()).ToHashSet();

        return Ok(new
        {
            serverNow = now,
            settings = new
            {
                settings.SameCategoryDiscountPercent,
                settings.PlatformFeePercent,
                settings.GlobalMinBidIncrement
            },
            totalLiveCount = allLive.Count,
            categories = categories.Select(c => new
            {
                c.Id,
                c.Name,
                c.Symbol,
                c.Description,
                liveCount = categoryCounts.GetValueOrDefault(c.Id, 0),
                hasSameCategoryDiscount = buyerPaidCategoryIds.Contains(c.Id)
            }),
            products = filtered.Select(a => new
            {
                a.Id,
                a.Name,
                a.CategoryId,
                categoryName = a.Category?.Name ?? "",
                a.SellerId,
                sellerName = a.Seller?.ShopName ?? a.Seller?.DisplayName ?? "",
                a.Condition,
                a.Description,
                a.ImageUrl,
                a.StartPrice,
                a.CurrentPrice,
                minIncrement = Math.Max(a.MinIncrement, settings.GlobalMinBidIncrement),
                a.StartAt,
                a.EndAt,
                hasStarted = a.StartAt <= now,
                bidCount = a.Bids.Count,
                saved = savedIds.Contains(a.Id),
                sameCategoryEligible = buyerPaidCategoryIds.Contains(a.CategoryId),
                a.CreatedAt
            })
        });
    }

    [HttpGet("auctions/{id:int}")]
    public async Task<IActionResult> GetAuctionDetail(int id)
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        var now = DateTime.UtcNow;

        var item = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Include(a => a.Bids).ThenInclude(b => b.Bidder)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (item is null)
            return NotFound(new { message = "ไม่พบสินค้านี้ในระบบ" });

        // Unapproved or rejected listings are visible ONLY to the seller (owner) or Admin
        if (item.Status is AuctionStatuses.Pending or AuctionStatuses.Rejected)
        {
            var canInspectPrivate = user is not null && (user.Id == item.SellerId || user.Role == Roles.Admin);
            if (!canInspectPrivate)
                return NotFound(new { message = "สินค้านี้ยังไม่เปิดเผยแพร่สาธารณะ" });
        }

        var sameCategoryEligible = user is not null && await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .AnyAsync(o => o.BuyerId == user.Id && o.PaymentStatus == PaymentStatuses.Paid && o.AuctionItem != null && o.AuctionItem.CategoryId == item.CategoryId);

        var effectiveIncrement = Math.Max(item.MinIncrement, settings.GlobalMinBidIncrement);
        var minNextBid = item.Bids.Count == 0
            ? item.StartPrice
            : item.CurrentPrice + effectiveIncrement;

        return Ok(new
        {
            item.Id,
            item.Name,
            item.CategoryId,
            categoryName = item.Category?.Name ?? "",
            item.SellerId,
            sellerName = item.Seller?.ShopName ?? item.Seller?.DisplayName ?? "",
            condition = item.Condition,
            item.Description,
            item.ImageUrl,
            item.StartPrice,
            item.CurrentPrice,
            minIncrement = effectiveIncrement,
            minNextBid,
            item.StartAt,
            item.EndAt,
            item.Status,
            hasStarted = item.StartAt <= now,
            isClosed = item.Status != AuctionStatuses.Approved || item.EndAt <= now,
            sameCategoryEligible,
            sameCategoryDiscountPercent = settings.SameCategoryDiscountPercent,
            bidCount = item.Bids.Count,
            history = item.Bids
                .OrderByDescending(b => b.Amount)
                .ThenByDescending(b => b.CreatedAt)
                .Take(20)
                .Select(b => new
                {
                    b.Id,
                    b.BidderId,
                    bidderName = b.Bidder?.DisplayName ?? "นักสะสม",
                    b.Amount,
                    b.CreatedAt
                })
        });
    }

    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("auctions/{id:int}/bid")]
    public async Task<IActionResult> PlaceBid(int id, [FromBody] PlaceBidRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized(new { message = "กรุณาเข้าสู่ระบบก่อนเสนอราคา" });

        var settings = await lifecycle.GetSettingsAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = DateTime.UtcNow;
            var item = await Db.AuctionItems
                .Include(a => a.Bids)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (item is null)
                return NotFound(new { message = "ไม่พบสินค้าประมูล" });

            var endUtc = item.EndAt.Kind == DateTimeKind.Utc ? item.EndAt : DateTime.SpecifyKind(item.EndAt, DateTimeKind.Utc);
            var startUtc = item.StartAt.Kind == DateTimeKind.Utc ? item.StartAt : DateTime.SpecifyKind(item.StartAt, DateTimeKind.Utc);

            if (item.Status != AuctionStatuses.Approved || endUtc <= now)
                return BadRequest(new { message = "รายการนี้ปิดประมูลแล้ว ไม่สามารถเสนอราคาได้" });
            if (startUtc > now)
                return BadRequest(new { message = "รายการนี้ยังไม่ถึงเวลาเริ่มประมูล" });
            if (item.SellerId == user.Id)
                return BadRequest(new { message = "ไม่สามารถเสนอราคาสินค้าของตัวเองได้" });

            var effectiveIncrement = Math.Max(item.MinIncrement, settings.GlobalMinBidIncrement);
            var minNext = item.Bids.Count == 0 ? item.StartPrice : item.CurrentPrice + effectiveIncrement;

            if (req.Amount < minNext)
                return BadRequest(new { message = $"ราคาเสนอต้องไม่ต่ำกว่า ฿{minNext:N0} (เพิ่มขั้นต่ำ ฿{effectiveIncrement:N0})" });

            var previousTopBid = item.Bids
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefault();

            var bid = new Bid
            {
                AuctionItemId = item.Id,
                BidderId = user.Id,
                Amount = req.Amount,
                CreatedAt = now
            };
            item.CurrentPrice = req.Amount;
            item.Version++;

            // Anti-sniping: extend auction end time if bid arrives within the final 2 minutes
            var extended = false;
            if ((endUtc - now) <= AntiSnipingWindow)
            {
                item.EndAt = now.Add(AntiSnipingExtension);
                extended = true;
            }

            Db.Bids.Add(bid);

            // Outbid notification to previous highest bidder (if different user)
            if (previousTopBid is not null && previousTopBid.BidderId != user.Id)
            {
                Db.Notifications.Add(new Notification
                {
                    UserId = previousTopBid.BidderId,
                    Type = "outbid",
                    Title = $"มีผู้เสนอราคาสูงกว่าคุณใน “{item.Name}”",
                    Body = $"ราคาสูงสุดใหม่ ฿{req.Amount:N0} (คุณเคยเสนอ ฿{previousTopBid.Amount:N0}) คลิกเพื่อเสนอราคาสู้",
                    TargetView = "today",
                    ReferenceId = item.Id,
                    IsRead = false,
                    CreatedAt = now
                });
            }

            try
            {
                await Db.SaveChangesAsync();
                var msg = extended
                    ? $"เสนอราคา ฿{req.Amount:N0} สำหรับ “{item.Name}” สำเร็จ (ต่อเวลาปิดประมูลอัตโนมัติอีก 2 นาที)"
                    : $"เสนอราคา ฿{req.Amount:N0} สำหรับ “{item.Name}” สำเร็จ";
                return Ok(new { message = msg, currentPrice = item.CurrentPrice, extended, newEndAt = item.EndAt });
            }
            catch (DbUpdateConcurrencyException)
            {
                Db.ChangeTracker.Clear();
                if (attempt == 2)
                    return Conflict(new { message = "มีผู้เสนอราคาเข้ามาพร้อมกัน กรุณาตรวจสอบราคาล่าสุดและลองใหม่อีกครั้ง" });
            }
        }

        return Conflict(new { message = "มีผู้เสนอราคาเข้ามาพร้อมกัน กรุณาลองใหม่อีกครั้ง" });
    }

    [Authorize]
    [HttpPost("auctions/{id:int}/save")]
    public async Task<IActionResult> ToggleSave(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var existing = await Db.SavedItems.FirstOrDefaultAsync(s => s.UserId == user.Id && s.AuctionItemId == id);
        if (existing is not null)
        {
            Db.SavedItems.Remove(existing);
            await Db.SaveChangesAsync();
            return Ok(new { saved = false, message = "นำออกจากรายการที่สนใจแล้ว" });
        }

        if (!await Db.AuctionItems.AnyAsync(a => a.Id == id && a.Status == AuctionStatuses.Approved))
            return NotFound(new { message = "ไม่พบสินค้าที่เปิดประมูล" });

        Db.SavedItems.Add(new SavedItem { UserId = user.Id, AuctionItemId = id, CreatedAt = DateTime.UtcNow });
        await Db.SaveChangesAsync();
        return Ok(new { saved = true, message = "บันทึกรายการที่สนใจแล้ว" });
    }

    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpGet("buyer/dashboard")]
    public async Task<IActionResult> GetBuyerDashboard()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var saved = await Db.SavedItems
            .Include(s => s.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(s => s.AuctionItem)!.ThenInclude(a => a!.Bids)
            .Where(s => s.UserId == user.Id && s.AuctionItem != null)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var myBids = await Db.Bids
            .Include(b => b.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(b => b.AuctionItem)!.ThenInclude(a => a!.Bids)
            .Where(b => b.BidderId == user.Id && b.AuctionItem != null)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var myOrders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Seller)
            .Where(o => o.BuyerId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var settings = await lifecycle.GetSettingsAsync();

        var convMap = await Db.Conversations
            .Where(c => c.BuyerId == user.Id)
            .ToDictionaryAsync(c => c.AuctionItemId, c => c.Id);

        return Ok(new
        {
            paymentConfig = new
            {
                settings.PlatformPromptPayId,
                settings.PlatformAccountName,
                settings.SameCategoryDiscountPercent,
                settings.PlatformFeePercent
            },
            saved = saved.Select(s => new
            {
                s.AuctionItem!.Id,
                s.AuctionItem.Name,
                categoryName = s.AuctionItem.Category?.Name ?? "",
                s.AuctionItem.Condition,
                s.AuctionItem.ImageUrl,
                s.AuctionItem.CurrentPrice,
                s.AuctionItem.EndAt,
                s.AuctionItem.Status,
                bidCount = s.AuctionItem.Bids.Count
            }),
            bidHistory = myBids.Select(b =>
            {
                var topBid = b.AuctionItem!.Bids.OrderByDescending(x => x.Amount).ThenBy(x => x.CreatedAt).FirstOrDefault();
                var isLeading = topBid?.Id == b.Id;
                var isClosed = b.AuctionItem.Status == AuctionStatuses.Closed || b.AuctionItem.EndAt <= DateTime.UtcNow;
                var statusText = isClosed
                    ? (b.AuctionItem.WinnerId == user.Id && isLeading ? "ชนะประมูล" : "ปิดประมูลแล้ว")
                    : (isLeading ? "ราคาสูงสุด" : "มีผู้เสนอสูงกว่า");
                return new
                {
                    b.Id,
                    auctionItemId = b.AuctionItemId,
                    itemName = b.AuctionItem.Name,
                    categoryName = b.AuctionItem.Category?.Name ?? "",
                    b.Amount,
                    currentPrice = b.AuctionItem.CurrentPrice,
                    isLeading,
                    isClosed,
                    statusText,
                    b.CreatedAt
                };
            }),
            orders = myOrders.Select(o => new
            {
                o.Id,
                o.OrderCode,
                o.AuctionItemId,
                itemName = o.AuctionItem?.Name ?? "",
                categoryName = o.AuctionItem?.Category?.Name ?? "",
                imageUrl = o.AuctionItem?.ImageUrl ?? "",
                o.SellerId,
                sellerName = o.Seller?.ShopName ?? o.Seller?.DisplayName ?? "",
                sellerPhone = o.Seller?.Phone ?? "",
                sellerBank = o.Seller?.BankAccount ?? "-",
                o.WinningPrice,
                o.DiscountPercent,
                o.DiscountReason,
                o.DiscountAmount,
                o.FinalBuyerAmount,
                o.PaymentMethod,
                o.PaymentStatus,
                o.PaymentSlipUrl,
                o.PaymentNote,
                o.PaymentSubmittedAt,
                o.PaymentRejectReason,
                o.PaymentDueAt,
                o.PaidAt,
                o.MeetupStatus,
                o.MeetupLocation,
                o.MeetupAt,
                o.MeetupNote,
                o.DeliveredAt,
                o.ReceivedAt,
                o.PayoutStatus,
                o.FinanceNote,
                conversationId = convMap.GetValueOrDefault(o.AuctionItemId, 0),
                o.CreatedAt
            })
        });
    }

    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("buyer/orders/{orderId:int}/pay")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> PayOrder(
        int orderId,
        [FromForm] string? paymentMethod,
        [FromForm] string? note,
        [FromForm] IFormFile? slipFile)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == user.Id);

        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });
        if (order.PaymentStatus != PaymentStatuses.Unpaid)
            return BadRequest(new { message = "รายการนี้แจ้งชำระเงินไปแล้ว ถูกยกเลิก หรืออยู่ระหว่างรอตรวจสอบ" });
        if (order.PaymentDueAt.HasValue && order.PaymentDueAt.Value <= DateTime.UtcNow)
            return BadRequest(new { message = "รายการนี้เกินกำหนดเวลาชำระเงินแล้ว" });

        var method = paymentMethod == PaymentMethods.DirectToSeller
            ? PaymentMethods.DirectToSeller
            : PaymentMethods.PlatformEscrow;

        if (method == PaymentMethods.PlatformEscrow && (slipFile is null || slipFile.Length == 0))
            return BadRequest(new { message = "การชำระผ่าน QR ระบบกลาง กรุณาแนบภาพสลิปโอนเงินเพื่อให้ฝ่ายการเงินตรวจสอบ" });

        string? savedSlipUrl = null;
        if (slipFile is not null && slipFile.Length > 0)
        {
            if (slipFile.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "ไฟล์สลิปต้องมีขนาดไม่เกิน 5 MB" });

            var ext = Path.GetExtension(slipFile.FileName).ToLowerInvariant();
            if (!AllowedSlipExtensions.Contains(ext))
                return BadRequest(new { message = "รองรับเฉพาะไฟล์รูปภาพ .jpg, .jpeg, .png หรือ .webp เท่านั้น" });

            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            var slipsDir = Path.Combine(webRoot, "uploads", "slips");
            Directory.CreateDirectory(slipsDir);

            var fileName = $"slip-{order.Id}-{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(slipsDir, fileName);
            await using (var fs = System.IO.File.Create(fullPath))
            {
                await slipFile.CopyToAsync(fs);
            }
            savedSlipUrl = $"/uploads/slips/{fileName}";
        }

        if (order.AuctionItem is not null)
        {
            var pricing = await lifecycle.CalculatePricingAsync(user.Id, order.AuctionItem.CategoryId, order.WinningPrice, order.Id);
            order.DiscountPercent = pricing.discountPercent;
            order.DiscountReason = pricing.reason;
            order.DiscountAmount = pricing.discountAmount;
            order.FinalBuyerAmount = pricing.finalBuyerAmount;
            if (method == PaymentMethods.PlatformEscrow)
            {
                order.PlatformFeePercent = pricing.feePercent;
                order.PlatformFeeAmount = pricing.feeAmount;
                order.SellerNetAmount = pricing.sellerNet;
            }
            else
            {
                order.PlatformFeePercent = 0;
                order.PlatformFeeAmount = 0;
                order.SellerNetAmount = pricing.finalBuyerAmount;
            }
        }

        var now = DateTime.UtcNow;
        order.PaymentMethod = method;
        order.PaymentStatus = PaymentStatuses.PendingVerification;
        order.PaymentSlipUrl = savedSlipUrl;
        order.PaymentNote = note?.Trim();
        order.PaymentSubmittedAt = now;
        order.PaymentRejectReason = null;

        var conv = await Db.Conversations.FirstOrDefaultAsync(
            c => c.AuctionItemId == order.AuctionItemId && c.BuyerId == order.BuyerId && c.SellerId == order.SellerId);
        if (conv is null)
        {
            conv = new Conversation
            {
                AuctionItemId = order.AuctionItemId,
                BuyerId = order.BuyerId,
                SellerId = order.SellerId,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.Conversations.Add(conv);
        }

        if (method == PaymentMethods.PlatformEscrow)
        {
            var financeIds = await Db.Users
                .Where(u => u.Role == Roles.Finance && !u.IsSuspended)
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var fid in financeIds)
            {
                Db.Notifications.Add(new Notification
                {
                    UserId = fid,
                    Type = "payment_verify",
                    Title = $"มีสลิป QR ระบบกลางรอตรวจ ({order.OrderCode})",
                    Body = $"{user.DisplayName} แนบสลิปยอด ฿{order.FinalBuyerAmount:N0} สำหรับ “{order.AuctionItem?.Name}”",
                    TargetView = "finance-payments",
                    ReferenceId = order.Id,
                    IsRead = false,
                    CreatedAt = now
                });
            }
        }
        else
        {
            Db.Notifications.Add(new Notification
            {
                UserId = order.SellerId,
                Type = "payment_verify",
                Title = $"ผู้ซื้อแจ้งชำระเงินตรง ({order.OrderCode})",
                Body = $"{user.DisplayName} แจ้งชำระยอด ฿{order.FinalBuyerAmount:N0} สำหรับ “{order.AuctionItem?.Name}” กรุณาตรวจสอบและกดยืนยัน",
                TargetView = "seller-orders",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });
        }

        await Db.SaveChangesAsync();

        var msg = method == PaymentMethods.PlatformEscrow
            ? $"ส่งสลิปยอด ฿{order.FinalBuyerAmount:N0} ให้ฝ่ายการเงินตรวจสอบแล้ว (ระบบคุ้มครองเงินแบบ Shopee)"
            : $"แจ้งการชำระเงินตรงยอด ฿{order.FinalBuyerAmount:N0} ให้ผู้ขายแล้ว รอผู้ขายกดยืนยัน";
        return Ok(new { message = msg });
    }

    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("buyer/orders/{orderId:int}/confirm-received")]
    public async Task<IActionResult> ConfirmReceived(int orderId)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการ" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ต้องยืนยันการชำระเงินก่อนจึงจะยืนยันรับสินค้าได้" });
        if (order.MeetupStatus == MeetupStatuses.Completed)
            return BadRequest(new { message = "คุณยืนยันรับสินค้าไปแล้ว" });

        order.MeetupStatus = MeetupStatuses.Completed;
        order.ReceivedAt = DateTime.UtcNow;
        order.DeliveredAt ??= DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ยืนยันการรับสินค้าเรียบร้อยแล้ว ฝ่ายการเงินสามารถปล่อยเงินให้ผู้ขายได้" });
    }
}
