// ============================================================================
// ไฟล์: Controllers/AuctionsController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend REST API Controller) - จัดการตลาดประมูล การเสนอราคา และแดชบอร์ดฝั่งผู้ซื้อ
// หน้าที่หลัก:
//   - แสดงรายการสินค้าประมูลที่กำลังเปิดอยู่ (รองรับกรองหมวดหมู่, ค้นหาคำสำคัญ, เรียงลำดับ และคำนวณสิทธิ์ส่วนลดหมวดเดียวกัน)
//   - แสดงรายละเอียดสินค้าประมูลรายชิ้น พร้อมประวัติการบิดสูงสุด 20 รายการล่าสุด และราคาเสนอขั้นต่ำถัดไป
//   - รับการเสนอราคา (Place Bid) พร้อม Optimistic Concurrency Retry (ป้องกันชนกัน), Anti-Sniping (ต่อเวลา 2 นาทีท้าย),
//     และแจ้งเตือนผู้ถูกบิดแซง (Outbid Notification)
//   - บันทึก/ยกเลิกรายการสินค้าที่สนใจ (Watchlist / Toggle Save)
//   - แดชบอร์ดฝั่งผู้ซื้อ: รายการที่สนใจ, ประวัติบิด, รายการชนะประมูล/ชำระเงิน/นัดรับ
//   - ส่งหลักฐานการชำระเงิน (แบบ QR ระบบกลาง Escrow แนบสลิป หรือ จ่ายตรงผู้ขาย) และยืนยันการรับสินค้าแล้ว
// การเชื่อมต่อกับเลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): `wwwroot/js/site.js` เรียกผ่าน `api()` (ใช้ `fetch()` + คุกกี้ `OldThingsAuth`)
//     ในหน้า "ประมูลวันนี้", หน้าต่างรายละเอียดสินค้า/บิด (`auctionDialog`), หน้า "รายการที่สนใจ & ประวัติบิด",
//     หน้า "ชำระเงิน & นัดรับ" และหน้าต่างชำระเงิน (`openCheckoutModal` ส่ง `multipart/form-data`)
//   - เลเยอร์ฐานข้อมูล/บริการ: ใช้ `AppDbContext`, `AuctionLifecycleService` (กติกา/คำนวณส่วนลดและค่าธรรมเนียม),
//     และ `IWebHostEnvironment` (จัดเก็บไฟล์ภาพสลิปโอนเงินลง `wwwroot/uploads/slips/`)
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// กำหนด Route หลักของ Controller นี้เป็น /api (รองรับทั้ง /api/auctions/... และ /api/buyer/...)
[Route("api")]
public class AuctionsController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    // นามสกุลไฟล์รูปภาพสลิปโอนเงินที่อนุญาตให้อัปโหลด
    private static readonly string[] AllowedSlipExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    // ช่วงเวลาท้ายก่อนปิดประมูล (2 นาที) ที่หากมีคนบิดเข้ามาจะเข้าเงื่อนไข Anti-Sniping
    private static readonly TimeSpan AntiSnipingWindow = TimeSpan.FromMinutes(2);
    // ระยะเวลาที่ต่อเพิ่มให้โดยอัตโนมัติ (2 นาที) เมื่อมีการบิดช่วงท้าย
    private static readonly TimeSpan AntiSnipingExtension = TimeSpan.FromMinutes(2);

    // DTO สำหรับรับจำนวนเงินที่ผู้ซื้อเสนอราคาประมูล
    public record PlaceBidRequest(decimal Amount);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/auctions (สาธารณะ: ดูได้ทั้งผู้ที่ยังไม่ล็อกอินและผู้ที่ล็อกอินแล้ว)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `loadAuctions()` (หน้าประมูลวันนี้, กดหมวดหมู่, พิมพ์ค้นหา, เปลี่ยนการเรียงลำดับ)
    //   และ `loadSellerDashboard()` (เรียกเพื่อโหลดแคชหมวดหมู่สินค้าถ้ายังไม่มี)
    // ข้อมูลที่รับ (Input): Query Params `?categoryId=&search=&sort=ending|newest` + คุกกี้ `OldThingsAuth` (ถ้าล็อกอินเพื่อเช็ค saved และสิทธิ์ส่วนลด)
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { serverNow, settings, totalLiveCount, categories, products }
    [HttpGet("auctions")]
    public async Task<IActionResult> GetAuctions([FromQuery] int? categoryId, [FromQuery] string? search, [FromQuery] string? sort)
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        var now = DateTime.UtcNow;

        // ดึงหมวดหมู่สินค้าทั้งหมดที่เปิดใช้งานอยู่
        var categories = await Db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .ToListAsync();

        // ดึงสินค้าประมูลที่ได้รับการอนุมัติ (Approved) และยังไม่หมดเวลาปิดประมูล
        var allLive = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatuses.Approved && a.EndAt > now)
            .ToListAsync();

        // นับจำนวนสินค้าที่กำลังเปิดประมูลแยกตามแต่ละหมวดหมู่เพื่อแสดงบนปุ่มตัวกรองหมวดหมู่
        var categoryCounts = allLive.GroupBy(a => a.CategoryId).ToDictionary(g => g.Key, g => g.Count());

        // กรองรายการตามหมวดหมู่ (ถ้าผู้ใช้เลือกหมวดหมู่เฉพาะ)
        IEnumerable<AuctionItem> filtered = allLive;
        if (categoryId.HasValue && categoryId.Value > 0)
            filtered = filtered.Where(a => a.CategoryId == categoryId.Value);

        // กรองตามคำค้นหา (ค้นหาได้จากชื่อสินค้า, สภาพสินค้า, รายละเอียด, และชื่อหมวดหมู่)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(a =>
                a.Name.ToLowerInvariant().Contains(term) ||
                a.Condition.ToLowerInvariant().Contains(term) ||
                a.Description.ToLowerInvariant().Contains(term) ||
                (a.Category?.Name.ToLowerInvariant().Contains(term) ?? false));
        }

        // เรียงลำดับรายการ: "newest" = ลงขายใหม่ล่าสุดก่อน, ค่าเริ่มต้น = ใกล้ปิดประมูลที่สุดก่อน
        filtered = sort == "newest"
            ? filtered.OrderByDescending(a => a.CreatedAt)
            : filtered.OrderBy(a => a.EndAt);

        // หากผู้ใช้ล็อกอินอยู่ ให้ดึงชุดรหัสสินค้าที่กดบันทึกหัวใจ (SavedItems) เอาไว้
        var savedIds = user is null
            ? new HashSet<int>()
            : (await Db.SavedItems.Where(s => s.UserId == user.Id).Select(s => s.AuctionItemId).ToListAsync()).ToHashSet();

        // ตรวจสอบสิทธิ์ส่วนลดหมวดเดียวกัน: จะได้สิทธิ์เฉพาะหมวดที่ผู้ซื้อเคยมีออเดอร์สถานะ "ชำระเงินแล้ว (Paid)" อย่างน้อย 1 รายการ
        // Eligible for same-category discount only when buyer already has a PAID order in that category
        var buyerPaidCategoryIds = user is null
            ? new HashSet<int>()
            : (await Db.OrderPayments
                .Include(o => o.AuctionItem)
                .Where(o => o.BuyerId == user.Id && o.PaymentStatus == PaymentStatuses.Paid && o.AuctionItem != null)
                .Select(o => o.AuctionItem!.CategoryId)
                .Distinct()
                .ToListAsync()).ToHashSet();

        // ส่งเวลาเซิร์ฟเวอร์ กติการะบบ รายการหมวดหมู่พร้อมตัวนับ และรายการสินค้าประมูลกลับไปยังหน้าบ้าน
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/auctions/{id} (สาธารณะสำหรับสินค้าที่อนุมัติแล้ว; สินค้า Pending/Rejected ดูได้เฉพาะเจ้าของหรือ Admin)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `openProduct(productId)` (เปิดหน้าต่างรายละเอียดสินค้า & ฟอร์มเสนอราคา)
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสสินค้า) + คุกกี้ `OldThingsAuth` (ถ้ามี)
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON รายละเอียดสินค้า, `minIncrement`, `minNextBid`, `sameCategoryEligible`, และ `history` (20 บิดล่าสุด)
    //   หรือ HTTP 404 (ไม่พบสินค้าหรือยังไม่เปิดเผยแพร่สาธารณะ)
    [HttpGet("auctions/{id:int}")]
    public async Task<IActionResult> GetAuctionDetail(int id)
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        var now = DateTime.UtcNow;

        // ดึงข้อมูลสินค้าพร้อมหมวดหมู่ ผู้ขาย และประวัติผู้เสนอราคาทั้งหมด
        var item = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Seller)
            .Include(a => a.Bids).ThenInclude(b => b.Bidder)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (item is null)
            return NotFound(new { message = "ไม่พบสินค้านี้ในระบบ" });

        // ป้องกันการเข้าดูสินค้าที่ยังรออนุมัติ (Pending) หรือถูกปฏิเสธ (Rejected): อนุญาตเฉพาะผู้ขายเจ้าของสินค้าหรือแอดมินเท่านั้น
        // Unapproved or rejected listings are visible ONLY to the seller (owner) or Admin
        if (item.Status is AuctionStatuses.Pending or AuctionStatuses.Rejected)
        {
            var canInspectPrivate = user is not null && (user.Id == item.SellerId || user.Role == Roles.Admin);
            if (!canInspectPrivate)
                return NotFound(new { message = "สินค้านี้ยังไม่เปิดเผยแพร่สาธารณะ" });
        }

        // ตรวจสอบว่าผู้ใช้ปัจจุบันมีสิทธิ์ได้ส่วนลดหมวดเดียวกันสำหรับสินค้าชิ้นนี้หรือไม่
        var sameCategoryEligible = user is not null && await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .AnyAsync(o => o.BuyerId == user.Id && o.PaymentStatus == PaymentStatuses.Paid && o.AuctionItem != null && o.AuctionItem.CategoryId == item.CategoryId);

        // คำนวณขั้นต่ำในการเพิ่มราคาต่อครั้ง (ใช้ค่าที่สูงกว่าระหว่างของสินค้าเองกับกติกากลาง) และราคาเสนอถัดไปขั้นต่ำ
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
            // ส่งประวัติการเสนอราคาสูงสุด 20 รายการล่าสุด เรียงจากราคาสูงสุดและเวลาล่าสุด
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auctions/{id}/bid (เฉพาะ Buyer, Seller ที่ล็อกอินแล้ว)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#bidForm` (submit) ภายในหน้าต่าง `auctionDialog`
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสสินค้า) + คุกกี้ `OldThingsAuth` + JSON Body (`PlaceBidRequest`: { amount })
    // ข้อมูลที่ส่งกลับ (Output):
    //   - HTTP 200 JSON { message, currentPrice, extended, newEndAt }
    //   - HTTP 400 (ปิดประมูลแล้ว / ยังไม่เริ่ม / บิดสินค้าตัวเอง / ราคาต่ำกว่าขั้นต่ำ)
    //   - HTTP 401 (ยังไม่ล็อกอิน), HTTP 404 (ไม่พบสินค้า), HTTP 409 (มีการบิดชนกันเกิน 3 รอบ)
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("auctions/{id:int}/bid")]
    public async Task<IActionResult> PlaceBid(int id, [FromBody] PlaceBidRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized(new { message = "กรุณาเข้าสู่ระบบก่อนเสนอราคา" });

        var settings = await lifecycle.GetSettingsAsync();

        // วนลูปรองรับการลองใหม่สูงสุด 3 ครั้ง (Optimistic Concurrency Retry) กรณีมีหลายคนกดบิดเข้ามาพร้อมกัน
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = DateTime.UtcNow;
            var item = await Db.AuctionItems
                .Include(a => a.Bids)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (item is null)
                return NotFound(new { message = "ไม่พบสินค้าประมูล" });

            // แปลงเวลาเริ่มและสิ้นสุดให้เป็น UTC อย่างชัดเจนก่อนเทียบกับเวลาปัจจุบัน
            var endUtc = item.EndAt.Kind == DateTimeKind.Utc ? item.EndAt : DateTime.SpecifyKind(item.EndAt, DateTimeKind.Utc);
            var startUtc = item.StartAt.Kind == DateTimeKind.Utc ? item.StartAt : DateTime.SpecifyKind(item.StartAt, DateTimeKind.Utc);

            // ตรวจสอบกฎการประมูล: ต้องได้รับอนุมัติ, ยังไม่หมดเวลา, ถึงเวลาเริ่มแล้ว, และห้ามบิดสินค้าของตนเอง
            if (item.Status != AuctionStatuses.Approved || endUtc <= now)
                return BadRequest(new { message = "รายการนี้ปิดประมูลแล้ว ไม่สามารถเสนอราคาได้" });
            if (startUtc > now)
                return BadRequest(new { message = "รายการนี้ยังไม่ถึงเวลาเริ่มประมูล" });
            if (item.SellerId == user.Id)
                return BadRequest(new { message = "ไม่สามารถเสนอราคาสินค้าของตัวเองได้" });

            // คำนวณราคาเสนอขั้นต่ำรอบนี้ (ถ้ายังไม่มีใครบิด ใช้ StartPrice; ถ้ามีแล้วต้องบวกเพิ่มอย่างน้อย effectiveIncrement)
            var effectiveIncrement = Math.Max(item.MinIncrement, settings.GlobalMinBidIncrement);
            var minNext = item.Bids.Count == 0 ? item.StartPrice : item.CurrentPrice + effectiveIncrement;

            if (req.Amount < minNext)
                return BadRequest(new { message = $"ราคาเสนอต้องไม่ต่ำกว่า ฿{minNext:N0} (เพิ่มขั้นต่ำ ฿{effectiveIncrement:N0})" });

            // ค้นหาผู้ที่ถือราคาสูงสุดก่อนหน้า เพื่อนำไปส่งแจ้งเตือนเมื่อถูกบิดแซง (Outbid)
            var previousTopBid = item.Bids
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefault();

            // สร้างรายการบิดใหม่ อัปเดตราคาปัจจุบัน และเพิ่มค่า Version (Concurrency Token) ของสินค้า
            var bid = new Bid
            {
                AuctionItemId = item.Id,
                BidderId = user.Id,
                Amount = req.Amount,
                CreatedAt = now
            };
            item.CurrentPrice = req.Amount;
            item.Version++;

            // กลไกป้องกันการปาดหน้าวินาทีสุดท้าย (Anti-Sniping): หากบิดเข้ามาในช่วง 2 นาทีสุดท้าย ให้ต่อเวลาปิดประมูลออกไปอีก 2 นาที
            // Anti-sniping: extend auction end time if bid arrives within the final 2 minutes
            var extended = false;
            if ((endUtc - now) <= AntiSnipingWindow)
            {
                item.EndAt = now.Add(AntiSnipingExtension);
                extended = true;
            }

            Db.Bids.Add(bid);

            // สร้าง Notification แจ้งเตือนผู้ที่เคยนำอยู่ก่อนหน้าว่ามีคนเสนอราคาสูงกว่าแล้ว (กรณีเป็นคนละบัญชี)
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
                // บันทึกลงฐานข้อมูล — หากมีคนอื่นแก้ไข `item.Version` ตัดหน้าไปก่อน EF Core จะโยน DbUpdateConcurrencyException
                await Db.SaveChangesAsync();
                var msg = extended
                    ? $"เสนอราคา ฿{req.Amount:N0} สำหรับ “{item.Name}” สำเร็จ (ต่อเวลาปิดประมูลอัตโนมัติอีก 2 นาที)"
                    : $"เสนอราคา ฿{req.Amount:N0} สำหรับ “{item.Name}” สำเร็จ";
                return Ok(new { message = msg, currentPrice = item.CurrentPrice, extended, newEndAt = item.EndAt });
            }
            catch (DbUpdateConcurrencyException)
            {
                // ล้างแคชของ EF Core แล้ววนรอบอ่านราคาล่าสุดจากฐานข้อมูลใหม่เพื่อลองบันทึกอีกครั้ง
                Db.ChangeTracker.Clear();
                if (attempt == 2)
                    return Conflict(new { message = "มีผู้เสนอราคาเข้ามาพร้อมกัน กรุณาตรวจสอบราคาล่าสุดและลองใหม่อีกครั้ง" });
            }
        }

        return Conflict(new { message = "มีผู้เสนอราคาเข้ามาพร้อมกัน กรุณาลองใหม่อีกครั้ง" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auctions/{id}/save (ต้องล็อกอิน: [Authorize])
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ปุ่มหัวใจ `[data-save]` บนการ์ดสินค้าหน้าประมูลวันนี้และหน้ารายการที่สนใจ
    // ข้อมูลที่รับ (Input): Route Param `id` (รหัสสินค้า) + คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { saved: true|false, message }, HTTP 401, HTTP 404 (ไม่พบสินค้าที่เปิดประมูล)
    [Authorize]
    [HttpPost("auctions/{id:int}/save")]
    public async Task<IActionResult> ToggleSave(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าผู้ใช้เคยบันทึกสินค้าชิ้นนี้ไว้แล้วหรือยัง (ทำงานแบบสลับสถานะ Toggle)
        var existing = await Db.SavedItems.FirstOrDefaultAsync(s => s.UserId == user.Id && s.AuctionItemId == id);
        if (existing is not null)
        {
            // หากเคยบันทึกไว้แล้ว -> ลบออกจากรายการที่สนใจ
            Db.SavedItems.Remove(existing);
            await Db.SaveChangesAsync();
            return Ok(new { saved = false, message = "นำออกจากรายการที่สนใจแล้ว" });
        }

        // ตรวจสอบว่าสินค้ามีอยู่จริงและอยู่ในสถานะเปิดประมูล (Approved)
        if (!await Db.AuctionItems.AnyAsync(a => a.Id == id && a.Status == AuctionStatuses.Approved))
            return NotFound(new { message = "ไม่พบสินค้าที่เปิดประมูล" });

        // เพิ่มลงตาราง SavedItems
        Db.SavedItems.Add(new SavedItem { UserId = user.Id, AuctionItemId = id, CreatedAt = DateTime.UtcNow });
        await Db.SaveChangesAsync();
        return Ok(new { saved = true, message = "บันทึกรายการที่สนใจแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/buyer/dashboard (เฉพาะ Buyer, Seller ที่ล็อกอินแล้ว)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `loadBuyerDashboard()` (หน้า "รายการที่สนใจ & ประวัติบิด" และหน้า "ชำระเงิน & นัดรับ")
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { paymentConfig, saved, bidHistory, orders } หรือ HTTP 401
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpGet("buyer/dashboard")]
    public async Task<IActionResult> GetBuyerDashboard()
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // 1) ดึงรายการสินค้าที่ผู้ใช้กดบันทึกหัวใจ (SavedItems)
        var saved = await Db.SavedItems
            .Include(s => s.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(s => s.AuctionItem)!.ThenInclude(a => a!.Bids)
            .Where(s => s.UserId == user.Id && s.AuctionItem != null)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        // 2) ดึงประวัติการเสนอราคาทั้งหมดของผู้ใช้นี้
        var myBids = await Db.Bids
            .Include(b => b.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(b => b.AuctionItem)!.ThenInclude(a => a!.Bids)
            .Where(b => b.BidderId == user.Id && b.AuctionItem != null)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        // 3) ดึงรายการออเดอร์ที่ผู้ใช้ชนะประมูลทั้งหมด พร้อมรายละเอียดการชำระเงินและนัดรับ
        var myOrders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Seller)
            .Where(o => o.BuyerId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var settings = await lifecycle.GetSettingsAsync();

        // สร้างแผนที่จับคู่ AuctionItemId -> ConversationId เพื่อใช้แสดงปุ่ม "แชท / แจ้งปัญหา" ในแต่ละออเดอร์
        var convMap = await Db.Conversations
            .Where(c => c.BuyerId == user.Id)
            .ToDictionaryAsync(c => c.AuctionItemId, c => c.Id);

        return Ok(new
        {
            // ข้อมูลบัญชีกลาง PromptPay และกติกาส่วนลดสำหรับสร้าง QR Code บนหน้าบ้าน
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
            // แปลงประวัติการบิดพร้อมคำนวณสถานะสด (ราคาสูงสุด / มีผู้เสนอสูงกว่า / ชนะประมูล / ปิดประมูลแล้ว)
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
            // รายการออเดอร์ทั้งหมดพร้อมราคาสุทธิ ส่วนลด สลิป สถานะนัดรับ และลิงก์ห้องแชท
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/buyer/orders/{orderId}/pay (เฉพาะ Buyer, Seller; จำกัดขนาด Request ไม่เกิน 6 MB)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#checkoutPayForm` ภายใน `openCheckoutModal(order)`
    // ข้อมูลที่รับ (Input): Route Param `orderId` + คุกกี้ `OldThingsAuth` + `multipart/form-data`
    //   (`paymentMethod`: "PlatformEscrow"|"DirectToSeller", `note`?, `slipFile`?: ไฟล์ภาพสลิป ≤ 5 MB)
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message }, HTTP 400 (ข้อมูล/ไฟล์ไม่ผ่านเงื่อนไขหรือเกินกำหนดชำระ), HTTP 401, HTTP 404
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

        // ค้นหาออเดอร์และตรวจสอบว่าเป็นของผู้ซื้อคนนี้จริง
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == user.Id);

        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });
        // อนุญาตให้ส่งชำระเงินได้เฉพาะออเดอร์ที่สถานะยังเป็น Unpaid และยังไม่เกินเวลาที่กำหนด
        if (order.PaymentStatus != PaymentStatuses.Unpaid)
            return BadRequest(new { message = "รายการนี้แจ้งชำระเงินไปแล้ว ถูกยกเลิก หรืออยู่ระหว่างรอตรวจสอบ" });
        if (order.PaymentDueAt.HasValue && order.PaymentDueAt.Value <= DateTime.UtcNow)
            return BadRequest(new { message = "รายการนี้เกินกำหนดเวลาชำระเงินแล้ว" });

        // กำหนดช่องทางการชำระเงิน: จ่ายตรงผู้ขาย (DirectToSeller) หรือ QR ระบบกลางคุ้มครองเงิน (PlatformEscrow)
        var method = paymentMethod == PaymentMethods.DirectToSeller
            ? PaymentMethods.DirectToSeller
            : PaymentMethods.PlatformEscrow;

        // หากเลือก QR ระบบกลาง (Escrow) บังคับต้องแนบภาพสลิปเพื่อให้ฝ่ายการเงินตรวจสอบ
        if (method == PaymentMethods.PlatformEscrow && (slipFile is null || slipFile.Length == 0))
            return BadRequest(new { message = "การชำระผ่าน QR ระบบกลาง กรุณาแนบภาพสลิปโอนเงินเพื่อให้ฝ่ายการเงินตรวจสอบ" });

        // ตรวจสอบและบันทึกไฟล์ภาพสลิปโอนเงินลงโฟลเดอร์ wwwroot/uploads/slips/
        string? savedSlipUrl = null;
        if (slipFile is not null && slipFile.Length > 0)
        {
            // จำกัดขนาดไฟล์ไม่เกิน 5 MB
            if (slipFile.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "ไฟล์สลิปต้องมีขนาดไม่เกิน 5 MB" });

            // ตรวจสอบนามสกุลไฟล์ให้เป็นรูปภาพที่อนุญาตเท่านั้น
            var ext = Path.GetExtension(slipFile.FileName).ToLowerInvariant();
            if (!AllowedSlipExtensions.Contains(ext))
                return BadRequest(new { message = "รองรับเฉพาะไฟล์รูปภาพ .jpg, .jpeg, .png หรือ .webp เท่านั้น" });

            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            var slipsDir = Path.Combine(webRoot, "uploads", "slips");
            Directory.CreateDirectory(slipsDir);

            // ตั้งชื่อไฟล์ด้วยรหัสออเดอร์ + GUID เพื่อป้องกันชื่อซ้ำและป้องกัน Path Traversal
            var fileName = $"slip-{order.Id}-{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(slipsDir, fileName);
            await using (var fs = System.IO.File.Create(fullPath))
            {
                await slipFile.CopyToAsync(fs);
            }
            savedSlipUrl = $"/uploads/slips/{fileName}";
        }

        // คำนวณราคา ส่วนลดหมวดเดียวกัน และค่าธรรมเนียมแพลตฟอร์มใหม่อีกครั้งตามช่องทางที่เลือก
        if (order.AuctionItem is not null)
        {
            var pricing = await lifecycle.CalculatePricingAsync(user.Id, order.AuctionItem.CategoryId, order.WinningPrice, order.Id);
            order.DiscountPercent = pricing.discountPercent;
            order.DiscountReason = pricing.reason;
            order.DiscountAmount = pricing.discountAmount;
            order.FinalBuyerAmount = pricing.finalBuyerAmount;
            if (method == PaymentMethods.PlatformEscrow)
            {
                // ระบบกลาง Escrow: หักค่าธรรมเนียมแพลตฟอร์มตามอัตราที่ฝ่ายการเงินตั้งไว้
                order.PlatformFeePercent = pricing.feePercent;
                order.PlatformFeeAmount = pricing.feeAmount;
                order.SellerNetAmount = pricing.sellerNet;
            }
            else
            {
                // จ่ายตรงผู้ขาย: ไม่หักค่าธรรมเนียมระบบกลาง ผู้ขายรับยอดสุทธิเต็มจำนวน
                order.PlatformFeePercent = 0;
                order.PlatformFeeAmount = 0;
                order.SellerNetAmount = pricing.finalBuyerAmount;
            }
        }

        // อัปเดตสถานะออเดอร์เป็น "รอตรวจสอบการชำระเงิน (PendingVerification)" และล้างเหตุผลการปฏิเสธครั้งก่อน (ถ้ามี)
        var now = DateTime.UtcNow;
        order.PaymentMethod = method;
        order.PaymentStatus = PaymentStatuses.PendingVerification;
        order.PaymentSlipUrl = savedSlipUrl;
        order.PaymentNote = note?.Trim();
        order.PaymentSubmittedAt = now;
        order.PaymentRejectReason = null;

        // สร้างห้องแชทระหว่างผู้ซื้อและผู้ขายให้อัตโนมัติ (ถ้ายังไม่มี) เพื่อใช้สื่อสารและนัดรับสินค้า
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

        // ส่ง Notification แจ้งเตือนผู้มีหน้าที่ตรวจสอบตามช่องทางที่ผู้ซื้อเลือก
        if (method == PaymentMethods.PlatformEscrow)
        {
            // ช่องทาง Escrow -> แจ้งเตือนฝ่ายการเงิน (Finance) ทุกคนให้เข้ามาตรวจสลิป
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
            // ช่องทางจ่ายตรงผู้ขาย -> แจ้งเตือนผู้ขายให้ตรวจสอบยอดและกดยืนยันรับเงิน
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/buyer/orders/{orderId}/confirm-received (เฉพาะ Buyer, Seller ที่ล็อกอินแล้ว)
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ปุ่ม `[data-confirm-received]` ("ยืนยันรับสินค้าแล้ว") ในตารางออเดอร์ของผู้ซื้อ
    // ข้อมูลที่รับ (Input): Route Param `orderId` + คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message }, HTTP 400 (ยังไม่ชำระเงินหรือยืนยันไปแล้ว), HTTP 401, HTTP 404
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("buyer/orders/{orderId:int}/confirm-received")]
    public async Task<IActionResult> ConfirmReceived(int orderId)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ค้นหาออเดอร์และตรวจสอบว่าเป็นของผู้ซื้อคนนี้จริง
        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการ" });
        // ต้องผ่านการยืนยันชำระเงิน (Paid) แล้ว และยังไม่เคยยืนยันรับสินค้ามาก่อน
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ต้องยืนยันการชำระเงินก่อนจึงจะยืนยันรับสินค้าได้" });
        if (order.MeetupStatus == MeetupStatuses.Completed)
            return BadRequest(new { message = "คุณยืนยันรับสินค้าไปแล้ว" });

        // ปรับสถานะนัดรับเป็น Completed และบันทึกเวลารับของ (ปลดล็อกให้ฝ่ายการเงินปล่อยเงิน Escrow ให้ผู้ขายได้)
        order.MeetupStatus = MeetupStatuses.Completed;
        order.ReceivedAt = DateTime.UtcNow;
        order.DeliveredAt ??= DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ยืนยันการรับสินค้าเรียบร้อยแล้ว ฝ่ายการเงินสามารถปล่อยเงินให้ผู้ขายได้" });
    }
}
