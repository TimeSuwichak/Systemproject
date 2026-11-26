// =================================================================================================
// ไฟล์: AppProject/Controllers/SellerController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend - REST API Controller) สำหรับระบบ "ผู้ขาย (Seller)"
// หน้าที่โดยรวม:
//   - ดึงข้อมูลแดชบอร์ดผู้ขาย (สรุปยอดขาย/รายได้, รายการสินค้าที่ลงประมูล, และคำสั่งซื้อทั้งหมด)
//   - รับใบสมัครเป็นผู้ขาย / แก้ไขข้อมูลร้านค้า (ใช้ร่วมกันทั้งผู้ประมูล Buyer และผู้ขาย Seller)
//   - อัปโหลดรูปภาพสินค้าลงโฟลเดอร์ wwwroot/uploads/products
//   - สร้าง แก้ไข และยกเลิกรายการสินค้าประมูล (CRUD เฉพาะสินค้าของตนเอง)
//   - จัดการคำสั่งซื้อฝั่งผู้ขาย: กำหนดนัดรับสินค้า (พร้อมส่งข้อความแชทอัตโนมัติ), ยืนยันส่งมอบสินค้า,
//     และตรวจสอบ/ยืนยันการชำระเงินแบบโอนตรงให้ผู้ขาย (DirectToSeller)
// การเชื่อมต่อกับไฟล์/เลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): เชื่อมต่อจาก wwwroot/js/site.js ผ่านฟังก์ชัน api() (ครอบ fetch())
//     โดยเบราว์เซอร์จะแนบ Cookie ยืนยันตัวตนชื่อ "OldThingsAuth" อัตโนมัติ (credentials: "same-origin")
//     และแสดงผลใน Views/Home/Index.cshtml (หน้า #view-sell, #view-seller-orders, #view-apply-seller)
//   - ฝั่งหลังบ้าน (Backend): สืบทอดจาก ApiBaseController (ใช้เมธอด GetCurrentActiveUserAsync, MapUserSummary),
//     ติดต่อฐานข้อมูลผ่าน AppDbContext, และใช้บริการ AuctionLifecycleService สำหรับดึงตั้งค่าการเงินและคำนวณยอดค้างชำระใหม่
// =================================================================================================

using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// กำหนดสิทธิ์ระดับคลาส: อนุญาตเฉพาะผู้ใช้ที่มี Role = "Seller" (ยกเว้นบาง Endpoint ที่ระบุ [Authorize] ทับด้านล่าง)
[Authorize(Roles = Roles.Seller)]
// กำหนดเส้นทางหลัก (Base Route) ของ Controller นี้เป็น /api/seller
[Route("api/seller")]
public class SellerController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    // รายการนามสกุลไฟล์รูปภาพที่อนุญาตให้อัปโหลด (ป้องกันการอัปโหลดไฟล์อันตราย เช่น .exe, .js, .cshtml)
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    // DTO สำหรับรับข้อมูลใบสมัครผู้ขาย หรืออัปเดตข้อมูลร้านค้า (JSON Request Body)
    public record SellerApplyRequest(string ShopName, string Phone, string BankAccount, string? SellerBio);

    // DTO สำหรับรับข้อมูลสินค้าประมูลตอนสร้างใหม่ (POST) และแก้ไข (PUT)
    public record ListingPayload(
        string Name,            // ชื่อสินค้า
        int CategoryId,         // รหัสหมวดหมู่สินค้า (FK -> Categories)
        string Condition,       // สภาพสินค้า เช่น ดีเยี่ยม, ใช้งานปกติ
        string Description,     // รายละเอียดและประวัติของเก่า
        string? ImageUrl,       // URL รูปภาพ (อาจได้จาก /api/seller/upload-image หรือใส่ลิงก์เอง)
        decimal StartPrice,     // ราคาเริ่มต้นประมูล
        decimal MinIncrement,   // ขั้นต่ำการเสนอราคาเพิ่มต่อครั้ง
        DateTime StartAt,       // วัน-เวลาเริ่มเปิดประมูล
        DateTime EndAt);        // วัน-เวลาปิดประมูล

    // DTO สำหรับรับข้อมูลกำหนดการนัดรับสินค้าจากผู้ขาย
    public record ScheduleMeetupRequest(string Location, DateTime MeetupAt, string? Note);

    // DTO สำหรับรับผลการตรวจสอบยอดโอนตรงจากผู้ซื้อ (อนุมัติหรือปฏิเสธพร้อมเหตุผล)
    public record VerifyDirectPaymentRequest(bool Approve, string? Reason);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : GET /api/seller/dashboard
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟังก์ชัน loadSellerDashboard()
    //                        แสดงผลใน Views/Home/Index.cshtml หน้า #view-sell และ #view-seller-orders
    // ข้อมูลที่รับจากหน้าบ้าน : ไม่มีพารามิเตอร์ (ยืนยันตัวตนผ่าน Cookie "OldThingsAuth", สิทธิ์ Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON ประกอบด้วยข้อมูลผู้ขาย (seller), กติกาขั้นต่ำ/ค่าธรรมเนียมกลาง,
    //                        สรุปยอดขายและเงินรอปล่อย (summary), รายการสินค้าพร้อมประวัติบิด (listings),
    //                        และคำสั่งซื้อทั้งหมดของผู้ขายพร้อม conversationId (orders)
    //                        หรือ 401 Unauthorized หากไม่พบผู้ใช้ที่ใช้งานอยู่
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        // ตรวจสอบผู้ใช้ปัจจุบันจาก Cookie Auth และเช็กว่าบัญชีไม่ได้ถูกระงับ
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ดึงการตั้งค่ากลางของระบบการเงิน (ค่าธรรมเนียมแพลตฟอร์ม, ขั้นต่ำการบิดกลาง)
        var settings = await lifecycle.GetSettingsAsync();

        // ดึงรายการสินค้าทั้งหมดที่ผู้ขายคนนี้ลงประมูล พร้อมข้อมูลหมวดหมู่ ผู้ชนะ และรายการบิดทั้งหมด
        var listings = await Db.AuctionItems
            .Include(a => a.Category)
            .Include(a => a.Winner)
            .Include(a => a.Bids).ThenInclude(b => b.Bidder)
            .Where(a => a.SellerId == user.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        // ดึงรายการคำสั่งซื้อ (OrderPayments) ที่เกิดจากสินค้าของผู้ขายคนนี้ พร้อมข้อมูลสินค้า หมวดหมู่ และผู้ซื้อ
        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Where(o => o.SellerId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        // สร้างพจนานุกรมจับคู่ (AuctionItemId, BuyerId) -> ConversationId เพื่อใช้เปิดห้องแชทจากตารางออเดอร์ได้ทันที
        var convMap = await Db.Conversations
            .Where(c => c.SellerId == user.Id)
            .ToDictionaryAsync(c => (c.AuctionItemId, c.BuyerId), c => c.Id);

        // คำนวณยอดสรุปทางการเงินของผู้ขาย:
        // 1) ยอดขายรวมทั้งหมดเฉพาะรายการที่ชำระเงินแล้ว (Paid)
        var totalGross = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid).Sum(o => o.FinalBuyerAmount);
        // 2) ค่าธรรมเนียมระบบรวม (คิดเฉพาะรายการที่จ่ายผ่านระบบกลาง PlatformEscrow และยืนยันชำระแล้ว)
        var totalFees = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid && o.PaymentMethod == PaymentMethods.PlatformEscrow).Sum(o => o.PlatformFeeAmount);
        // 3) ยอดเงินสุทธิที่ได้รับแล้ว (ทั้งกรณีฝ่ายการเงินปล่อยเงิน Released และกรณีผู้ซื้อโอนตรง DirectSettled)
        var totalReleased = orders.Where(o => o.PayoutStatus == PayoutStatuses.Released || o.PayoutStatus == PayoutStatuses.DirectSettled).Sum(o => o.SellerNetAmount);
        // 4) ยอดเงินสุทธิที่ยังพักอยู่ในระบบกลาง (Escrow) รอฝ่ายการเงินปล่อยเงิน
        var pendingPayout = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid && o.PayoutStatus == PayoutStatuses.Pending && o.PaymentMethod == PaymentMethods.PlatformEscrow).Sum(o => o.SellerNetAmount);

        // ส่งข้อมูล JSON ทั้งหมดกลับไปให้หน้าบ้านเรนเดอร์แดชบอร์ดผู้ขาย
        return Ok(new
        {
            seller = MapUserSummary(user),
            globalMinBidIncrement = settings.GlobalMinBidIncrement,
            platformFeePercent = settings.PlatformFeePercent,
            summary = new
            {
                totalListings = listings.Count,
                // นับสินค้าที่ได้รับอนุมัติและยังไม่หมดเวลาประมูล
                activeListings = listings.Count(l => l.Status == AuctionStatuses.Approved && l.EndAt > DateTime.UtcNow),
                totalGross,
                totalFees,
                totalReleased,
                pendingPayout
            },
            // แปลงรายการสินค้าเป็นโครงสร้าง JSON ที่หน้าบ้านใช้งานง่าย
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
                // อนุญาตให้แก้ไขหรือยกเลิกได้เฉพาะตอนที่ยังรออนุมัติ (Pending) หรือถูกปฏิเสธ (Rejected)
                canEditOrCancel = a.Status is AuctionStatuses.Pending or AuctionStatuses.Rejected,
                winnerName = a.Winner?.DisplayName,
                bidCount = a.Bids.Count,
                // เรียงประวัติการเสนอราคาจากยอดสูงสุดลงมา เพื่อให้ผู้ขายกดดูใน Modal ได้
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
            // แปลงรายการคำสั่งซื้อเป็นโครงสร้าง JSON สำหรับหน้า #view-seller-orders
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
                // ธงบอกหน้าบ้านว่าให้แสดงปุ่ม "ยืนยันรับเงินตรง / ปฏิเสธ" ได้หรือไม่
                canVerifyDirectPayment = o.PaymentMethod == PaymentMethods.DirectToSeller && o.PaymentStatus == PaymentStatuses.PendingVerification,
                // รหัสห้องแชทที่ผูกกับสินค้าและผู้ซื้อรายนี้ (ถ้าไม่มีจะคืน 0)
                conversationId = convMap.GetValueOrDefault((o.AuctionItemId, o.BuyerId), 0),
                o.CreatedAt
            })
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/apply
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟอร์ม #buyerApplySellerForm (หน้า #view-apply-seller สำหรับ Buyer)
    //                        และฟอร์ม #sellerReapplyForm (หน้า #view-sell สำหรับ Seller ที่ถูกปฏิเสธและส่งสมัครใหม่)
    // ข้อมูลที่รับจากหน้าบ้าน : JSON Body (SellerApplyRequest: shopName, phone, bankAccount, sellerBio)
    //                        แนบ Cookie "OldThingsAuth" (สิทธิ์ Role = Buyer หรือ Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message, user } เพื่ออัปเดตสถานะผู้ขายฝั่งหน้าบ้าน
    //                        หรือ 400 BadRequest (กรอกไม่ครบ), 401 Unauthorized
    // Consolidated single apply-seller endpoint for BOTH Buyer and Seller
    [Authorize(Roles = $"{Roles.Buyer},{Roles.Seller}")]
    [HttpPost("apply")]
    public async Task<IActionResult> UpdateApplication([FromBody] SellerApplyRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบข้อมูลบังคับ: ชื่อร้าน เบอร์ติดต่อ และเลขบัญชีรับเงินต้องไม่ว่าง
        if (string.IsNullOrWhiteSpace(req.ShopName) || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.BankAccount))
            return BadRequest(new { message = "กรุณากรอกข้อมูลร้าน เบอร์โทร และเลขบัญชีรับเงินให้ครบถ้วน" });

        // อัปเดตข้อมูลโปรไฟล์ร้านค้าและตัดช่องว่างหัว-ท้าย
        user.ShopName = req.ShopName.Trim();
        user.Phone = req.Phone.Trim();
        user.BankAccount = req.BankAccount.Trim();
        user.SellerBio = req.SellerBio?.Trim();
        // หากยังไม่ได้รับอนุมัติเป็นผู้ขาย ให้เปลี่ยนสถานะเป็น Pending เพื่อเข้าคิวรอแอดมินตรวจสอบและล้างเหตุผลที่เคยถูกปฏิเสธ
        if (user.SellerStatus != SellerStatuses.Approved)
        {
            user.SellerStatus = SellerStatuses.Pending;
            user.SellerRejectReason = null;
            user.SellerAppliedAt = DateTime.UtcNow;
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "ส่งใบสมัครผู้ขายให้แอดมินตรวจสอบแล้ว", user = MapUserSummary(user) });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/upload-image
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "change" ของ input #listingImageFile (ฟอร์มลงสินค้าใหม่ใน #view-sell)
    //                        และ input #editListingImgFile (Modal แก้ไขสินค้า #editListingForm)
    // ข้อมูลที่รับจากหน้าบ้าน : multipart/form-data (FormData ที่มีฟิลด์ "file" เป็น IFormFile, จำกัดคำขอสูงสุด 6 MB)
    //                        แนบ Cookie "OldThingsAuth" (สิทธิ์ Role = Seller และต้องผ่านการอนุมัติแล้ว)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { imageUrl: "/uploads/products/...", message } เพื่อให้หน้าบ้านนำไปแสดงพรีวิวและใส่ในฟอร์ม
    //                        หรือ 400 BadRequest (ไม่เลือกไฟล์/ขนาดเกิน 5 MB/นามสกุลไม่รองรับ), 401 Unauthorized, 403 Forbidden
    [HttpPost("upload-image")]
    [RequestSizeLimit(6_000_000)] // จำกัดขนาด Request รวมไม่เกิน 6 MB ระดับ ASP.NET Core
    public async Task<IActionResult> UploadProductImage([FromForm] IFormFile? file)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();
        // ต้องเป็นผู้ขายที่แอดมินอนุมัติแล้วเท่านั้นจึงจะอัปโหลดรูปสินค้าได้
        if (user.SellerStatus != SellerStatuses.Approved)
            return StatusCode(403, new { message = "บัญชีผู้ขายยังไม่ได้รับการอนุมัติ" });

        // ตรวจสอบความถูกต้องของไฟล์: ห้ามว่าง และขนาดไฟล์ต้องไม่เกิน 5 MB
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "กรุณาเลือกไฟล์รูปภาพ" });
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "ไฟล์รูปภาพต้องมีขนาดไม่เกิน 5 MB" });

        // ตรวจสอบนามสกุลไฟล์ให้ตรงกับรายการที่อนุญาต (.jpg, .jpeg, .png, .webp)
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return BadRequest(new { message = "รองรับเฉพาะไฟล์ .jpg, .jpeg, .png หรือ .webp เท่านั้น" });

        // เตรียมโฟลเดอร์ปลายทาง wwwroot/uploads/products (สร้างให้อัตโนมัติหากยังไม่มี)
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var productsDir = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(productsDir);

        // ตั้งชื่อไฟล์ใหม่ด้วยรหัสผู้ขาย + GUID เพื่อป้องกันชื่อซ้ำและปัญหา Path Traversal
        var fileName = $"item-{user.Id}-{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(productsDir, fileName);
        // บันทึกสตรีมไฟล์ลงดิสก์แบบอะซิงโครนัส
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs);
        }

        // คืน Relative URL ให้หน้าบ้านเก็บลงฟิลด์ imageUrl และแสดงพรีวิวทันที
        return Ok(new { imageUrl = $"/uploads/products/{fileName}", message = "อัปโหลดรูปสินค้าเรียบร้อย" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/listings
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "submit" ของฟอร์ม #listingForm (หน้า #view-sell)
    // ข้อมูลที่รับจากหน้าบ้าน : JSON Body (ListingPayload: name, categoryId, condition, description, imageUrl,
    //                        startPrice, minIncrement, startAt, endAt) + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message, itemId } (สินค้าจะอยู่ในสถานะ Pending รอแอดมินอนุมัติ)
    //                        หรือ 400 BadRequest (ข้อมูลไม่ผ่านกฎการตรวจสอบ), 401 Unauthorized, 403 Forbidden (ผู้ขายยังไม่อนุมัติ)
    [HttpPost("listings")]
    public async Task<IActionResult> CreateListing([FromBody] ListingPayload req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ป้องกันผู้ขายที่ยังไม่ได้รับอนุมัติลงประกาศสินค้า
        if (user.SellerStatus != SellerStatuses.Approved)
            return StatusCode(403, new { message = "บัญชีผู้ขายของคุณยังไม่ได้รับการอนุมัติจากแอดมิน" });

        // ตรวจสอบความครบถ้วนของข้อมูล ราคา ขั้นต่ำการบิด ช่วงเวลา และหมวดหมู่
        var validationError = await ValidateListingPayloadAsync(req);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        // แปลงวัน-เวลาเริ่มและสิ้นสุดให้เป็น UTC เสมอก่อนบันทึกลงฐานข้อมูล
        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();
        // หากผู้ขายไม่ได้อัปโหลด/ระบุรูปภาพ ให้ใช้รูปตัวอย่างเริ่มต้น (Fallback Image)
        var imageUrl = string.IsNullOrWhiteSpace(req.ImageUrl)
            ? "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80"
            : req.ImageUrl.Trim();

        // สร้างเอนทิตี AuctionItem ใหม่ โดยตั้งสถานะเริ่มต้นเป็น Pending เพื่อรอแอดมินตรวจสอบ
        var item = new AuctionItem
        {
            Name = req.Name.Trim(),
            CategoryId = req.CategoryId,
            SellerId = user.Id,
            Condition = req.Condition.Trim(),
            Description = req.Description.Trim(),
            ImageUrl = imageUrl,
            StartPrice = req.StartPrice,
            CurrentPrice = req.StartPrice, // ราคาปัจจุบันเริ่มต้นเท่ากับราคาเปิดประมูล
            MinIncrement = req.MinIncrement,
            StartAt = startUtc,
            EndAt = endUtc,
            Status = AuctionStatuses.Pending,
            Version = 1, // ใช้สำหรับ Optimistic Concurrency Control ตอนมีผู้ประมูลแข่งกัน
            CreatedAt = DateTime.UtcNow
        };

        Db.AuctionItems.Add(item);
        await Db.SaveChangesAsync();

        return Ok(new { message = "ส่งสินค้าให้แอดมินตรวจสอบแล้ว", itemId = item.Id });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : PUT /api/seller/listings/{id}
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "submit" ของฟอร์ม #editListingForm ใน Modal แก้ไขสินค้า
    //                        (เปิดจากปุ่มแก้ไขในตารางสินค้าของหน้า #view-sell)
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {id:int} (รหัสสินค้า) + JSON Body (ListingPayload) + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (รีเซ็ตสถานะกลับเป็น Pending เพื่อส่งให้แอดมินตรวจใหม่)
    //                        หรือ 400 BadRequest (สถานะไม่อนุญาตให้แก้ไข / ข้อมูลไม่ถูกต้อง), 401 Unauthorized, 404 NotFound
    [HttpPut("listings/{id:int}")]
    public async Task<IActionResult> UpdateListing(int id, [FromBody] ListingPayload req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ค้นหาสินค้าโดยบังคับต้องเป็นของ user.Id ปัจจุบันเท่านั้น (ป้องกันแก้ไขสินค้าของร้านอื่น)
        var item = await Db.AuctionItems.FirstOrDefaultAsync(a => a.Id == id && a.SellerId == user.Id);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้าของคุณ" });

        // อนุญาตให้แก้ไขเฉพาะสินค้าที่ยังรออนุมัติ (Pending) หรือถูกปฏิเสธ (Rejected) เท่านั้น
        if (item.Status is not (AuctionStatuses.Pending or AuctionStatuses.Rejected))
            return BadRequest(new { message = "แก้ไขได้เฉพาะสินค้าที่ยังรออนุมัติหรือถูกปฏิเสธเท่านั้น" });

        // ตรวจสอบความถูกต้องของข้อมูลที่ส่งมาใหม่
        var validationError = await ValidateListingPayloadAsync(req);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        // แปลงเวลาเริ่ม-สิ้นสุดเป็น UTC
        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();

        // อัปเดตข้อมูลสินค้าทั้งหมดและรีเซ็ตสถานะกลับไปรออนุมัติใหม่
        item.Name = req.Name.Trim();
        item.CategoryId = req.CategoryId;
        item.Condition = req.Condition.Trim();
        item.Description = req.Description.Trim();
        // อัปเดตรูปภาพเฉพาะกรณีมีการส่ง URL ใหม่มา (ถ้าเว้นว่างให้คงรูปเดิมไว้)
        if (!string.IsNullOrWhiteSpace(req.ImageUrl))
            item.ImageUrl = req.ImageUrl.Trim();
        item.StartPrice = req.StartPrice;
        item.CurrentPrice = req.StartPrice;
        item.MinIncrement = req.MinIncrement;
        item.StartAt = startUtc;
        item.EndAt = endUtc;
        item.Status = AuctionStatuses.Pending;
        item.RejectReason = null; // ล้างเหตุผลที่เคยถูกปฏิเสธออก
        item.Version++;           // เพิ่มเลขเวอร์ชันของรายการสินค้า

        await Db.SaveChangesAsync();
        return Ok(new { message = "บันทึกการแก้ไขสินค้าและส่งให้แอดมินตรวจสอบอีกครั้งแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : DELETE /api/seller/listings/{id}
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่มที่มีแอตทริบิวต์ [data-delete-listing] ในหน้า #view-sell
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {id:int} (รหัสสินค้าที่จะยกเลิก/ลบ) + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message }
    //                        หรือ 400 BadRequest (สถานะไม่อนุญาตหรือมีผู้เสนอราคาแล้ว), 401 Unauthorized, 404 NotFound
    [HttpDelete("listings/{id:int}")]
    public async Task<IActionResult> DeleteListing(int id)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // โหลดสินค้าพร้อมประวัติ Bids เพื่อเช็กสิทธิ์ความเป็นเจ้าของและตรวจว่ามีการบิดแล้วหรือไม่
        var item = await Db.AuctionItems
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id && a.SellerId == user.Id);
        if (item is null) return NotFound(new { message = "ไม่พบสินค้าของคุณ" });

        // ลบได้เฉพาะสินค้าที่ยังรออนุมัติหรือถูกปฏิเสธเท่านั้น และต้องยังไม่มีผู้เสนอราคา
        if (item.Status is not (AuctionStatuses.Pending or AuctionStatuses.Rejected))
            return BadRequest(new { message = "ยกเลิกได้เฉพาะสินค้าที่ยังรออนุมัติหรือถูกปฏิเสธเท่านั้น" });
        if (item.Bids.Count > 0)
            return BadRequest(new { message = "ไม่สามารถยกเลิกสินค้าที่มีการเสนอราคาแล้ว" });

        Db.AuctionItems.Remove(item);
        await Db.SaveChangesAsync();
        return Ok(new { message = $"ยกเลิกรายการสินค้า “{item.Name}” แล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/orders/{orderId}/meetup
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-schedule-meetup] เปิด Modal ฟอร์ม #meetupForm (หน้า #view-seller-orders)
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} + JSON Body (ScheduleMeetupRequest: location, meetupAt, note)
    //                        + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (พร้อมส่งข้อความแจ้งเวลานัดรับเข้าห้องแชทอัตโนมัติถ้ามีห้องแชทอยู่)
    //                        หรือ 400 BadRequest (ยังไม่ชำระเงิน / ไม่ระบุสถานที่), 401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/meetup")]
    public async Task<IActionResult> ScheduleMeetup(int orderId, [FromBody] ScheduleMeetupRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าคำสั่งซื้อมีอยู่จริงและเป็นของผู้ขายคนนี้
        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });
        // ต้องยืนยันการชำระเงินเรียบร้อยแล้วเท่านั้นจึงจะนัดรับได้
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ต้องรอให้ยืนยันการชำระเงินก่อนจึงจะกำหนดนัดรับได้" });
        if (string.IsNullOrWhiteSpace(req.Location))
            return BadRequest(new { message = "กรุณาระบุสถานที่นัดรับ" });

        // บันทึกสถานที่ วันเวลานัดรับ (UTC) และหมายเหตุลงในคำสั่งซื้อ
        order.MeetupLocation = req.Location.Trim();
        order.MeetupAt = req.MeetupAt.Kind == DateTimeKind.Utc ? req.MeetupAt : req.MeetupAt.ToUniversalTime();
        order.MeetupNote = req.Note?.Trim();
        // เปลี่ยนสถานะนัดรับจาก NotScheduled เป็น Scheduled
        if (order.MeetupStatus == MeetupStatuses.NotScheduled)
            order.MeetupStatus = MeetupStatuses.Scheduled;

        // ค้นหาห้องแชทระหว่างผู้ซื้อ-ผู้ขายของสินค้านี้ เพื่อส่งข้อความสรุปกำหนดการนัดรับเข้าห้องแชทให้อัตโนมัติ
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/orders/{orderId}/deliver
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-confirm-deliver] ในหน้า #view-seller-orders
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} (ไม่มี JSON Body) + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (เปลี่ยน MeetupStatus เป็น Delivered และบันทึกเวลา DeliveredAt
    //                        เพื่อเริ่มนับเวลารอผู้ซื้อยืนยันรับของหรือครบกำหนดปล่อยเงินอัตโนมัติ)
    //                        หรือ 400 BadRequest (ยังไม่จ่ายเงิน/รับของไปแล้ว), 401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/deliver")]
    public async Task<IActionResult> ConfirmDelivered(int orderId)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบว่าคำสั่งซื้อเป็นของผู้ขายรายนี้จริง
        var order = await Db.OrderPayments.FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "รายการนี้ยังไม่ได้ชำระเงิน" });
        if (order.MeetupStatus == MeetupStatuses.Completed)
            return BadRequest(new { message = "ผู้ซื้อยืนยันรับสินค้าเรียบร้อยแล้ว" });

        // อัปเดตสถานะเป็น "ส่งมอบแล้ว (Delivered)" และบันทึกเวลาส่งมอบ
        order.MeetupStatus = MeetupStatuses.Delivered;
        order.DeliveredAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ยืนยันส่งมอบสินค้าแล้ว รอผู้ซื้อยืนยันรับของเพื่อให้ฝ่ายการเงินปล่อยเงิน" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/seller/orders/{orderId}/verify-payment
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-seller-verify-pay] (ส่ง approve: true)
    //                        และปุ่ม [data-seller-reject-pay] (เปิด Modal กรอกเหตุผล ส่ง approve: false, reason) ในหน้า #view-seller-orders
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} + JSON Body (VerifyDirectPaymentRequest: approve, reason)
    //                        + Cookie "OldThingsAuth" (Role = Seller)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message }
    //                        - กรณี Approve: ปิดยอดชำระเป็น Paid + DirectSettled (ค่าธรรมเนียม 0%), แจ้งเตือนผู้ซื้อ, คำนวณส่วนลดออเดอร์ค้างใหม่
    //                        - กรณี Reject: คืนสถานะเป็น Unpaid พร้อมเหตุผลและแจ้งเตือนผู้ซื้อให้แจ้งชำระใหม่
    //                        หรือ 400 BadRequest (ไม่ใช่วิธีโอนตรง / สถานะไม่ตรง / ไม่ใส่เหตุผลปฏิเสธ), 401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/verify-payment")]
    public async Task<IActionResult> VerifyDirectPayment(int orderId, [FromBody] VerifyDirectPaymentRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // โหลดคำสั่งซื้อพร้อมข้อมูลสินค้า (ใช้แสดงชื่อสินค้าในการแจ้งเตือน) และตรวจสิทธิ์ความเป็นเจ้าของ
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == user.Id);
        if (order is null) return NotFound(new { message = "ไม่พบรายการคำสั่งซื้อ" });

        // ป้องกันการตรวจข้ามช่องทาง: ผู้ขายตรวจได้เฉพาะออเดอร์ที่ผู้ซื้อเลือก "โอนตรงผู้ขาย (DirectToSeller)" เท่านั้น
        if (order.PaymentMethod != PaymentMethods.DirectToSeller)
            return BadRequest(new { message = "รายการนี้ชำระผ่าน QR ระบบกลาง ต้องให้ฝ่ายการเงินเป็นผู้ตรวจสอบ" });
        // ต้องอยู่ในสถานะรอตรวจสอบยอดเท่านั้น
        if (order.PaymentStatus != PaymentStatuses.PendingVerification)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอผู้ขายยืนยันการชำระเงิน" });

        var now = DateTime.UtcNow;
        if (req.Approve)
        {
            // กรณีผู้ขายยืนยันได้รับเงินโอนตรงเรียบร้อย:
            // ปรับสถานะการชำระเป็น Paid และสถานะรับเงินผู้ขายเป็น DirectSettled (ไม่มีการหักค่าธรรมเนียมระบบ)
            order.PaymentStatus = PaymentStatuses.Paid;
            order.PaidAt = now;
            order.PayoutStatus = PayoutStatuses.DirectSettled;
            order.PayoutReleasedAt = now;
            order.PlatformFeePercent = 0;
            order.PlatformFeeAmount = 0;
            order.SellerNetAmount = order.FinalBuyerAmount;
            order.PaymentRejectReason = null;
            order.FinanceNote = "ผู้ขายยืนยันได้รับเงินชำระตรงจากผู้ซื้อแล้ว";

            // สร้างการแจ้งเตือน (Notification) ส่งไปยังผู้ซื้อให้ทราบว่าชำระเงินเรียบร้อยแล้ว
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
            // คำนวณส่วนลดหมวดเดียวกันให้กับออเดอร์อื่นที่ผู้ซื้อคนนี้ยังค้างชำระอยู่ (เนื่องจากมีออเดอร์ที่จ่ายแล้วเพิ่มขึ้นมา)
            await lifecycle.RecalculateUnpaidOrdersAsync(order.BuyerId);
            return Ok(new { message = $"ยืนยันได้รับเงินตรงสำหรับ {order.OrderCode} แล้ว" });
        }
        else
        {
            // กรณีผู้ขายปฏิเสธยอดชำระ: บังคับให้ระบุเหตุผลเพื่อให้ผู้ซื้อแก้ไขได้ถูกต้อง
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธการชำระเงิน" });

            // ดีดสถานะกลับเป็น Unpaid พร้อมบันทึกเหตุผล
            order.PaymentStatus = PaymentStatuses.Unpaid;
            order.PaymentRejectReason = req.Reason.Trim();

            // ส่งการแจ้งเตือนกลับไปยังผู้ซื้อให้ตรวจสอบและแจ้งชำระเงินใหม่
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

    // เมธอดช่วยตรวจสอบความถูกต้องของข้อมูลสินค้าประมูล (ใช้ร่วมกันทั้ง CreateListing และ UpdateListing)
    // คืนค่า null หากข้อมูลถูกต้องทั้งหมด หรือคืนข้อความแจ้งเตือนภาษาไทยหากพบข้อผิดพลาด
    private async Task<string?> ValidateListingPayloadAsync(ListingPayload req)
    {
        // ดึงกติกาขั้นต่ำการเสนอราคาจากฝ่ายการเงิน
        var settings = await lifecycle.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Condition) || string.IsNullOrWhiteSpace(req.Description))
            return "กรุณากรอกชื่อสินค้า สภาพสินค้า และรายละเอียดให้ครบถ้วน";
        if (req.StartPrice <= 0)
            return "ราคาเริ่มต้นต้องมากกว่า 0 บาท";
        if (req.MinIncrement < settings.GlobalMinBidIncrement)
            return $"ขั้นต่ำการเสนอราคาต้องไม่น้อยกว่า ฿{settings.GlobalMinBidIncrement:N0} ตามกติกาฝ่ายการเงิน";

        // ตรวจสอบความสมเหตุสมผลของช่วงเวลาประมูล (เวลาสิ้นสุดต้องหลังเวลาเริ่ม และต้องอยู่ในอนาคต)
        var startUtc = req.StartAt.Kind == DateTimeKind.Utc ? req.StartAt : req.StartAt.ToUniversalTime();
        var endUtc = req.EndAt.Kind == DateTimeKind.Utc ? req.EndAt : req.EndAt.ToUniversalTime();
        if (endUtc <= startUtc)
            return "เวลาสิ้นสุดประมูลต้องอยู่หลังเวลาเริ่มประมูล";
        if (endUtc <= DateTime.UtcNow)
            return "เวลาสิ้นสุดประมูลต้องอยู่ในอนาคต";

        // ตรวจสอบว่าหมวดหมู่ที่เลือกมีอยู่จริงและยังเปิดใช้งานอยู่ (IsActive)
        var categoryExists = await Db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.IsActive);
        if (!categoryExists)
            return "หมวดหมู่ที่เลือกไม่ถูกต้อง";

        return null;
    }
}
