// ============================================================================
// 📄 ชื่อไฟล์: AppProject/Services/AuctionLifecycleService.cs
// 🏷️ บทบาท: ฝั่งหลังบ้าน (Backend) — ชั้นตรรกะธุรกิจหลัก (Business Logic Service) และงานเบื้องหลัง (Background Service)
// 📝 หน้าที่โดยรวม:
//    จัดการวงจรชีวิตของการประมูลและการเงินทั้งหมด ได้แก่:
//    1) ตรวจเงื่อนไขการปล่อยเงินบัญชีกลาง Escrow (CanReleaseEscrow)
//    2) อ่าน/สร้างการตั้งค่าการเงินกลาง (GetSettingsAsync)
//    3) คำนวณราคาสุทธิ ส่วนลดซื้อหมวดเดียวกัน ค่าธรรมเนียมแพลตฟอร์ม และรายได้สุทธิผู้ขาย (CalculatePricingAsync / RecalculateUnpaidOrdersAsync)
//    4) ปิดประมูลสินค้าที่หมดเวลาอัตโนมัติ หาผู้ชนะ สร้างคำสั่งซื้อ เปิดห้องแชท และส่งแจ้งเตือน (CloseExpiredAuctionsAsync)
//    5) ยกเลิกคำสั่งซื้อที่เกินกำหนดชำระ 48 ชม. และส่งต่อสิทธิ์ให้ผู้เสนอราคาอันดับถัดไปอัตโนมัติ (ProcessExpiredPaymentsAsync)
//    6) งานเบื้องหลัง (AuctionClosingBackgroundService) ที่ทำงานทุก 15 วินาทีเพื่อเรียกขั้นตอนข้อ 4 และ 5 ต่อเนื่อง
// 🔄 การเชื่อมต่อกับส่วนอื่นในระบบ:
//    - ลงทะเบียนเป็น Scoped Service และ Hosted Service ใน AppProject/Program.cs
//    - เชื่อมต่อฐานข้อมูลผ่าน AppDbContext (AppProject/Data/AppDbContext.cs) และ Entity ใน AppProject/Models/Entities.cs
//    - ถูกเรียกใช้จาก AuctionsController, AuthController, SellerController, FinanceController และ NotificationsController
//    - 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] คำสั่งซื้อ ห้องแชท และการแจ้งเตือนที่สร้างจากไฟล์นี้จะถูกส่งไปยัง
//      ฝั่งหน้าบ้าน (wwwroot/js/site.js) เมื่อหน้าบ้าน Polling ไปที่ GET /api/notifications/summary หรือโหลดหน้า
//      GET /api/buyer/dashboard, GET /api/seller/dashboard, GET /api/finance/dashboard และ GET /api/chat/conversations
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Services;

// บริการหลักจัดการวงจรชีวิตการประมูล การคำนวณราคา/ส่วนลด และกฎการปล่อยเงิน Escrow
public class AuctionLifecycleService(AppDbContext db)
{
    public const int PaymentDeadlineHours = 48;          // ระยะเวลาให้ผู้ชนะชำระเงิน (48 ชั่วโมง) ก่อนถูกยกเลิกและส่งสิทธิ์ต่อ
    public const int AutoReleaseAfterDeliveredDays = 3;  // จำนวนวันหลังผู้ขายส่งมอบ (Delivered) ที่อนุญาตให้ฝ่ายการเงินปล่อยเงิน Escrow ได้แม้ผู้ซื้อยังไม่กดรับ
    // ตัวล็อกแบบ SemaphoreSlim (อนุญาตเข้าได้ทีละ 1 งาน) ป้องกันไม่ให้รอบงานเบื้องหลังหรือคำขอทำงานซ้อนกันจนสร้างออเดอร์ซ้ำ
    private static readonly SemaphoreSlim LifecycleLock = new(1, 1);

    // ------------------------------------------------------------------------
    // ตรวจสิทธิ์ว่าคำสั่งซื้อสามารถปล่อยเงินจากบัญชีกลาง Escrow ให้ผู้ขายได้หรือไม่
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    //    - ถูกเรียกใน FinanceController: GET /api/finance/dashboard (ส่งค่า canRelease ไปเปิด/ปิดปุ่มปล่อยเงินบนหน้าบ้าน)
    //      และ POST /api/finance/orders/{orderId}/release (ตรวจยืนยันซ้ำฝั่งเซิร์ฟเวอร์ก่อนปล่อยเงินจริง)
    //    - ถูกเรียกใน NotificationsController: GET /api/notifications/summary (นับจำนวนรายการพร้อมปล่อยเงินให้ฝ่ายการเงิน)
    // ------------------------------------------------------------------------
    public static bool CanReleaseEscrow(OrderPayment order, bool hasOpenDispute, DateTime utcNow)
    {
        // เงื่อนไขพื้นฐาน: ต้องเป็นการชำระผ่านบัญชีกลาง (Escrow), ชำระเงินแล้ว (Paid), ยังรอปล่อยเงิน (Pending) และต้องไม่มีข้อพิพาทค้างอยู่
        if (order.PaymentMethod != PaymentMethods.PlatformEscrow) return false;
        if (order.PaymentStatus != PaymentStatuses.Paid) return false;
        if (order.PayoutStatus != PayoutStatuses.Pending) return false;
        if (hasOpenDispute) return false;

        // กรณีที่ 1: ผู้ซื้อกดยืนยันได้รับสินค้าแล้ว (Completed) -> ปล่อยเงินได้ทันที
        if (order.MeetupStatus == MeetupStatuses.Completed)
            return true;

        // กรณีที่ 2: ผู้ขายกดส่งมอบแล้ว (Delivered) และเวลาผ่านไปครบ 3 วันขึ้นไปโดยไม่มีข้อพิพาท -> อนุญาตให้ปล่อยเงินได้
        if (order.MeetupStatus == MeetupStatuses.Delivered
            && order.DeliveredAt.HasValue
            && order.DeliveredAt.Value <= utcNow.AddDays(-AutoReleaseAfterDeliveredDays))
            return true;

        return false;
    }

    // ------------------------------------------------------------------------
    // ดึงข้อมูลการตั้งค่าการเงินกลาง (FinanceSettings แถว Id = 1) หากยังไม่มีจะสร้างค่าเริ่มต้นให้อัตโนมัติ
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ถูกเรียกจาก GET /api/auth/me, GET /api/auctions, GET /api/buyer/dashboard,
    //    GET /api/seller/dashboard, GET /api/finance/dashboard และ PUT /api/finance/settings เพื่อส่งอัตราส่วนลด/ค่าธรรมเนียม/พร้อมเพย์ไปหน้าบ้าน
    // ------------------------------------------------------------------------
    public async Task<FinanceSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await db.FinanceSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is not null) return settings;

        // สร้างเรคคอร์ดการตั้งค่าเริ่มต้นกรณีฐานข้อมูลยังไม่มีแถว Id = 1
        settings = new FinanceSettings
        {
            Id = 1,
            SameCategoryDiscountPercent = 10,
            PlatformFeePercent = 5,
            GlobalMinBidIncrement = 50,
            PlatformPromptPayId = "081-999-8888",
            PlatformAccountName = "บจก. ของเก่าเล่าเรื่อง (บัญชีกลาง)",
            UpdatedAt = DateTime.UtcNow
        };
        db.FinanceSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    // ------------------------------------------------------------------------
    // คำนวณราคาสุทธิ ส่วนลดซื้อหมวดเดียวกัน ค่าธรรมเนียมแพลตฟอร์ม และยอดสุทธิของผู้ขาย
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    //    - ถูกเรียกตอนปิดประมูล, ตอนส่งสิทธิ์ต่ออันดับถัดไป และตอนผู้ซื้อกดชำระเงิน (POST /api/buyer/orders/{orderId}/pay)
    //    - ผลลัพธ์ถูกบันทึกลง OrderPayment และส่งเป็น JSON แสดงรายละเอียดราคาและส่วนลดบนหน้าชำระเงิน (wwwroot/js/site.js)
    // ------------------------------------------------------------------------
    public async Task<(decimal discountPercent, string? reason, decimal discountAmount, decimal finalBuyerAmount, decimal feePercent, decimal feeAmount, decimal sellerNet)>
        CalculatePricingAsync(int buyerId, int categoryId, decimal winningPrice, int? excludeOrderId = null, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);

        // นับเฉพาะคำสั่งซื้อในหมวดเดียวกันที่ "ชำระเงินเรียบร้อยแล้ว (Paid)" เท่านั้น
        // เพื่อป้องกันกรณีชนะพร้อมกัน 2 ชิ้นที่ยังไม่จ่ายแล้วได้ส่วนลดซ้ำซ้อนทั้งคู่
        var paidSameCategoryCount = await db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.BuyerId == buyerId
                && o.PaymentStatus == PaymentStatuses.Paid
                && o.AuctionItem != null
                && o.AuctionItem.CategoryId == categoryId
                && (!excludeOrderId.HasValue || o.Id != excludeOrderId.Value)) // ไม่นับรวมคำสั่งซื้อปัจจุบันที่กำลังคำนวณ
            .CountAsync(ct);

        // มีสิทธิ์รับส่วนลดเมื่อเคยชำระเงินในหมวดนี้มาแล้วอย่างน้อย 1 ชิ้น และระบบตั้งค่าส่วนลด > 0%
        var eligible = paidSameCategoryCount >= 1 && settings.SameCategoryDiscountPercent > 0;
        var discountPercent = eligible ? settings.SameCategoryDiscountPercent : 0m;
        var discountAmount = Math.Round(winningPrice * discountPercent / 100m, 2); // ปัดเศษทศนิยม 2 ตำแหน่ง
        var finalBuyerAmount = Math.Max(0m, winningPrice - discountAmount);      // ยอดที่ผู้ซื้อต้องจ่ายจริง (ไม่ต่ำกว่า 0)
        var feePercent = settings.PlatformFeePercent;
        var feeAmount = Math.Round(finalBuyerAmount * feePercent / 100m, 2);     // ค่าธรรมเนียมคิดจากยอดสุทธิหลังหักส่วนลด
        var sellerNet = Math.Max(0m, finalBuyerAmount - feeAmount);              // ยอดสุทธิที่ผู้ขายจะได้รับหลังหักค่าธรรมเนียม
        var reason = eligible
            ? $"ส่วนลดซื้อหมวดเดียวกัน {discountPercent:0.##}% (ชำระแล้วในหมวดนี้ {paidSameCategoryCount} ชิ้น)"
            : null;

        return (discountPercent, reason, discountAmount, finalBuyerAmount, feePercent, feeAmount, sellerNet);
    }

    // ------------------------------------------------------------------------
    // คำนวณราคาและส่วนลดใหม่ให้กับคำสั่งซื้อที่ยังไม่ได้ชำระเงิน (Unpaid)
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    //    - ถูกเรียกจาก POST /api/finance/orders/{orderId}/verify-payment และ POST /api/seller/orders/{orderId}/verify-payment
    //      (ส่ง onlyBuyerId เพื่อให้ชิ้นอื่นในหมวดเดียวกันของผู้ซื้อคนนั้นได้ส่วนลดอัตโนมัติทันทีที่ชิ้นแรกยืนยันชำระเงินแล้ว)
    //    - ถูกเรียกจาก PUT /api/finance/settings (ไม่ระบุ onlyBuyerId เพื่ออัปเดตออเดอร์ Unpaid ทั้งระบบตามเรตใหม่)
    // ------------------------------------------------------------------------
    public async Task RecalculateUnpaidOrdersAsync(int? onlyBuyerId = null, CancellationToken ct = default)
    {
        // ค้นหาเฉพาะคำสั่งซื้อที่ยังไม่ชำระเงิน (Unpaid) และมีข้อมูลสินค้าเชื่อมโยงอยู่
        var query = db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.PaymentStatus == PaymentStatuses.Unpaid && o.AuctionItem != null);

        // หากระบุเฉพาะผู้ซื้อ ให้กรองคำนวณใหม่เฉพาะคำสั่งซื้อของผู้ซื้อรายนั้น
        if (onlyBuyerId.HasValue)
            query = query.Where(o => o.BuyerId == onlyBuyerId.Value);

        var unpaid = await query.ToListAsync(ct);
        if (unpaid.Count == 0) return;

        // วนลูปคำนวณราคา ส่วนลด และค่าธรรมเนียมใหม่ให้แต่ละคำสั่งซื้อ
        foreach (var order in unpaid)
        {
            var pricing = await CalculatePricingAsync(order.BuyerId, order.AuctionItem!.CategoryId, order.WinningPrice, order.Id, ct);
            order.DiscountPercent = pricing.discountPercent;
            order.DiscountReason = pricing.reason;
            order.DiscountAmount = pricing.discountAmount;
            order.FinalBuyerAmount = pricing.finalBuyerAmount;
            order.PlatformFeePercent = pricing.feePercent;
            order.PlatformFeeAmount = pricing.feeAmount;
            order.SellerNetAmount = pricing.sellerNet;
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------
    // ปิดการประมูลสินค้าที่ครบกำหนดเวลา (EndAt <= now) อัตโนมัติ
    // ขั้นตอน: เปลี่ยนสถานะเป็น Closed -> หาผู้เสนอราคาสูงสุด -> สร้าง OrderPayment (กำหนดจ่าย 48 ชม.)
    //         -> เปิดห้องแชทผู้ชนะ-ผู้ขาย -> ส่ง Notification ให้ทั้งผู้ชนะและผู้ขาย
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ทำงานเบื้องหลังทุก 15 วินาที ผลลัพธ์จะปรากฏบนหน้าบ้าน
    //    ผ่าน GET /api/auctions, GET /api/buyer/dashboard, GET /api/seller/dashboard และ GET /api/notifications/summary
    // ------------------------------------------------------------------------
    public async Task<int> CloseExpiredAuctionsAsync(CancellationToken ct = default)
    {
        // ล็อกไม่ให้มีหลายเธรดเข้ามาปิดประมูลพร้อมกัน
        await LifecycleLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            // ดึงสินค้าที่เปิดประมูลอยู่ (Approved) และเลยเวลาปิดประมูลแล้ว (EndAt <= now) พร้อมประวัติการบิดทั้งหมด
            var expired = await db.AuctionItems
                .Include(a => a.Bids)
                .Where(a => a.Status == AuctionStatuses.Approved && a.EndAt <= now)
                .ToListAsync(ct);

            if (expired.Count == 0) return 0;

            foreach (var auction in expired)
            {
                // เปลี่ยนสถานะสินค้าเป็น Closed และเพิ่มเลข Version เพื่อป้องกันการบิดชนกันในเสี้ยววินาทีสุดท้าย
                auction.Status = AuctionStatuses.Closed;
                auction.Version++;

                // หาผู้เสนอราคาสูงสุด (หากราคาเท่ากัน ให้คนที่เสนอราคาก่อนตาม CreatedAt เป็นผู้ชนะ)
                var highestBid = auction.Bids
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.CreatedAt)
                    .FirstOrDefault();

                // กรณีปิดประมูลโดยไม่มีผู้เสนอราคาเลย -> แจ้งเตือนผู้ขายให้สามารถนำสินค้าลงขายใหม่ได้
                if (highestBid is null)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = auction.SellerId,
                        Type = "auction_closed",
                        Title = $"ปิดประมูลแล้ว (ไม่มีผู้เสนอราคา): “{auction.Name}”",
                        Body = "สินค้าหมดเวลาประมูลโดยไม่มีผู้เสนอราคา คุณสามารถลงขายใหม่อีกครั้งได้",
                        TargetView = "sell", // คลิกแจ้งเตือนแล้วพาไปหน้าลงขายของฉัน
                        ReferenceId = auction.Id,
                        IsRead = false,
                        CreatedAt = now
                    });
                    continue;
                }

                // บันทึกผู้ชนะประมูลและราคาปิดจริง
                auction.WinnerId = highestBid.BidderId;
                auction.CurrentPrice = highestBid.Amount;

                // ตรวจกันซ้ำเผื่อมีคำสั่งซื้อของสินค้าและผู้ซื้อคู่นี้อยู่แล้ว
                var alreadyHasOrder = await db.OrderPayments.AnyAsync(
                    o => o.AuctionItemId == auction.Id && o.BuyerId == highestBid.BidderId, ct);
                if (alreadyHasOrder) continue;

                // คำนวณราคา ส่วนลดซื้อหมวดเดียวกัน และค่าธรรมเนียมแพลตฟอร์ม
                var pricing = await CalculatePricingAsync(highestBid.BidderId, auction.CategoryId, highestBid.Amount, null, ct);

                // สร้างคำสั่งซื้อใหม่ (OrderPayment) พร้อมกำหนดเส้นตายชำระเงิน 48 ชม. และตั้งค่าเริ่มต้นเป็นบัญชีกลาง Escrow
                var order = new OrderPayment
                {
                    OrderCode = $"AUC-{auction.Id:D5}", // รหัสคำสั่งซื้อ เช่น AUC-00001
                    AuctionItemId = auction.Id,
                    BuyerId = highestBid.BidderId,
                    SellerId = auction.SellerId,
                    WinningPrice = highestBid.Amount,
                    DiscountPercent = pricing.discountPercent,
                    DiscountReason = pricing.reason,
                    DiscountAmount = pricing.discountAmount,
                    FinalBuyerAmount = pricing.finalBuyerAmount,
                    PlatformFeePercent = pricing.feePercent,
                    PlatformFeeAmount = pricing.feeAmount,
                    SellerNetAmount = pricing.sellerNet,
                    PaymentMethod = PaymentMethods.PlatformEscrow,
                    PaymentStatus = PaymentStatuses.Unpaid,
                    PaymentDueAt = now.AddHours(PaymentDeadlineHours), // เส้นตายการชำระเงิน (48 ชม. นับจากปิดประมูล)
                    MeetupStatus = MeetupStatuses.NotScheduled,
                    PayoutStatus = PayoutStatuses.Pending,
                    CreatedAt = now
                };
                db.OrderPayments.Add(order);

                // สร้างห้องแชทระหว่างผู้ชนะกับผู้ขายอัตโนมัติ (ถ้ายังไม่มี) พร้อมข้อความทักทายเริ่มต้น
                await EnsureWinnerConversationAsync(auction, highestBid.BidderId, highestBid.Amount, now, ct);

                // แจ้งเตือนผู้ชนะประมูล (หน้าบ้านดึงผ่าน GET /api/notifications/summary และคลิกไปหน้า payments)
                db.Notifications.Add(new Notification
                {
                    UserId = highestBid.BidderId,
                    Type = "auction_won",
                    Title = $"ยินดีด้วย! คุณชนะประมูล “{auction.Name}”",
                    Body = $"ราคาปิด ฿{highestBid.Amount:N0} ยอดสุทธิ ฿{pricing.finalBuyerAmount:N0} กรุณาชำระเงินภายใน {PaymentDeadlineHours} ชม.",
                    TargetView = "payments",
                    ReferenceId = auction.Id,
                    IsRead = false,
                    CreatedAt = now
                });

                // แจ้งเตือนผู้ขายว่าปิดประมูลสำเร็จ (คลิกไปหน้า seller-orders)
                db.Notifications.Add(new Notification
                {
                    UserId = auction.SellerId,
                    Type = "auction_sold",
                    Title = $"ปิดประมูลสำเร็จ! “{auction.Name}” ขายได้ ฿{highestBid.Amount:N0}",
                    Body = $"มีผู้ชนะประมูลแล้ว ระบบให้เวลาชำระเงิน {PaymentDeadlineHours} ชม. ติดตามได้ที่หน้านัดรับ & ยอดขาย",
                    TargetView = "seller-orders",
                    ReferenceId = auction.Id,
                    IsRead = false,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            return expired.Count;
        }
        finally
        {
            // ปล่อยล็อกเสมอแม้เกิดข้อผิดพลาด เพื่อไม่ให้ระบบค้าง
            LifecycleLock.Release();
        }
    }

    // ------------------------------------------------------------------------
    // ตรวจสอบและจัดการคำสั่งซื้อที่เลยกำหนดชำระเงิน 48 ชม. (Overdue Unpaid Orders)
    // ขั้นตอน: ยกเลิกออเดอร์เดิม -> แจ้งเตือนผู้ซื้อเดิม -> หาผู้เสนอราคาอันดับถัดไป (Runner-up) ที่ยังไม่เคยได้สิทธิ์
    //         -> หากมี: โอนสิทธิ์ผู้ชนะ สร้างออเดอร์ใหม่ (-R2, -R3...) เปิดแชท และแจ้งเตือนผู้ซื้อใหม่+ผู้ขาย
    //         -> หากไม่มี: ล้าง WinnerId เป็น null และแจ้งผู้ขายให้นำสินค้าลงประมูลใหม่ได้
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ทำงานเบื้องหลังทุก 15 วินาที และสะท้อนผลทันทีบนหน้าบ้านผ่าน
    //    GET /api/notifications/summary, GET /api/buyer/dashboard และ GET /api/seller/dashboard
    // ------------------------------------------------------------------------
    public async Task<int> ProcessExpiredPaymentsAsync(CancellationToken ct = default)
    {
        // ล็อกป้องกันการทำงานซ้อนกับรอบงานอื่น
        await LifecycleLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            // ค้นหาคำสั่งซื้อที่ยัง Unpaid และเลยกำหนดเส้นตาย PaymentDueAt แล้ว พร้อมดึงข้อมูลสินค้าและรายการบิดทั้งหมด
            var overdueOrders = await db.OrderPayments
                .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Bids)
                .Where(o => o.PaymentStatus == PaymentStatuses.Unpaid
                    && o.PaymentDueAt != null
                    && o.PaymentDueAt <= now)
                .ToListAsync(ct);

            if (overdueOrders.Count == 0) return 0;

            foreach (var order in overdueOrders)
            {
                // ยกเลิกคำสั่งซื้อและการจ่ายเงินของผู้ชนะเดิมที่ไม่ชำระตามกำหนด
                order.PaymentStatus = PaymentStatuses.Cancelled;
                order.PayoutStatus = PayoutStatuses.Cancelled;
                order.FinanceNote = $"ยกเลิกอัตโนมัติ: ไม่ชำระเงินภายใน {PaymentDeadlineHours} ชม.";

                // แจ้งเตือนผู้ซื้อเดิมว่ารายการถูกยกเลิกเพราะหมดเวลาชำระเงิน
                db.Notifications.Add(new Notification
                {
                    UserId = order.BuyerId,
                    Type = "payment_expired",
                    Title = $"หมดเวลาชำระเงิน ({order.OrderCode})",
                    Body = $"รายการ “{order.AuctionItem?.Name}” ถูกยกเลิกเนื่องจากเกินกำหนดชำระ {PaymentDeadlineHours} ชม.",
                    TargetView = "payments",
                    ReferenceId = order.Id,
                    IsRead = false,
                    CreatedAt = now
                });

                var auction = order.AuctionItem;
                if (auction is null) continue;

                // รวบรวมรายชื่อผู้ซื้อทั้งหมดที่เคยได้รับคำสั่งซื้อในสินค้านี้ไปแล้ว (รวมผู้ซื้อที่เพิ่งถูกยกเลิก) เพื่อไม่ส่งสิทธิ์ซ้ำคนเดิม
                var excludedBuyerIds = await db.OrderPayments
                    .Where(o => o.AuctionItemId == auction.Id)
                    .Select(o => o.BuyerId)
                    .ToListAsync(ct);

                // คัดเลือกผู้เสนอราคาอันดับถัดไป (Runner-up):
                // ตัดคนที่เคยได้สิทธิ์ออก -> จัดกลุ่มตามผู้บิดแต่ละคนเอาบิดสูงสุดของคนนั้น -> เรียงจากราคาสูงสุด (ถ้าเท่ากันเอาคนบิดก่อน)
                var nextBidder = auction.Bids
                    .Where(b => !excludedBuyerIds.Contains(b.BidderId))
                    .GroupBy(b => b.BidderId)
                    .Select(g => g.OrderByDescending(b => b.Amount).ThenBy(b => b.CreatedAt).First())
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.CreatedAt)
                    .FirstOrDefault();

                if (nextBidder is not null)
                {
                    // นับรอบการโอนสิทธิ์ (เช่น รอบที่ 2 = -R2, รอบที่ 3 = -R3) และคำนวณราคา/ส่วนลดใหม่สำหรับผู้ซื้อคนใหม่
                    var attempt = excludedBuyerIds.Count + 1;
                    var pricing = await CalculatePricingAsync(nextBidder.BidderId, auction.CategoryId, nextBidder.Amount, null, ct);

                    // อัปเดตผู้ชนะและราคาปิดของสินค้าเป็นของผู้เสนอราคาอันดับถัดไป
                    auction.WinnerId = nextBidder.BidderId;
                    auction.CurrentPrice = nextBidder.Amount;
                    auction.Version++;

                    // สร้างคำสั่งซื้อใหม่ให้ผู้เสนอราคาอันดับถัดไป พร้อมให้เวลาชำระเงินใหม่ 48 ชม.
                    var runnerUpOrder = new OrderPayment
                    {
                        OrderCode = $"AUC-{auction.Id:D5}-R{attempt}", // รหัสออเดอร์ต่อท้าย -R2, -R3 เพื่อไม่ชน Unique Index
                        AuctionItemId = auction.Id,
                        BuyerId = nextBidder.BidderId,
                        SellerId = auction.SellerId,
                        WinningPrice = nextBidder.Amount,
                        DiscountPercent = pricing.discountPercent,
                        DiscountReason = pricing.reason,
                        DiscountAmount = pricing.discountAmount,
                        FinalBuyerAmount = pricing.finalBuyerAmount,
                        PlatformFeePercent = pricing.feePercent,
                        PlatformFeeAmount = pricing.feeAmount,
                        SellerNetAmount = pricing.sellerNet,
                        PaymentMethod = PaymentMethods.PlatformEscrow,
                        PaymentStatus = PaymentStatuses.Unpaid,
                        PaymentDueAt = now.AddHours(PaymentDeadlineHours),
                        MeetupStatus = MeetupStatuses.NotScheduled,
                        PayoutStatus = PayoutStatuses.Pending,
                        CreatedAt = now
                    };
                    db.OrderPayments.Add(runnerUpOrder);

                    // เปิดห้องแชทให้ผู้ชนะอันดับถัดไปกับผู้ขายโดยอัตโนมัติ
                    await EnsureWinnerConversationAsync(auction, nextBidder.BidderId, nextBidder.Amount, now, ct);

                    // แจ้งเตือนผู้เสนอราคาอันดับถัดไปว่าได้รับสิทธิ์ซื้อต่อ
                    db.Notifications.Add(new Notification
                    {
                        UserId = nextBidder.BidderId,
                        Type = "auction_won",
                        Title = $"ได้รับสิทธิ์ซื้อต่ออันดับถัดไป! “{auction.Name}”",
                        Body = $"ผู้ชนะเดิมไม่ชำระตามกำหนด คุณได้รับสิทธิ์ในราคา ฿{nextBidder.Amount:N0} (ยอดสุทธิ ฿{pricing.finalBuyerAmount:N0}) กรุณาชำระภายใน {PaymentDeadlineHours} ชม.",
                        TargetView = "payments",
                        ReferenceId = auction.Id,
                        IsRead = false,
                        CreatedAt = now
                    });

                    // แจ้งเตือนผู้ขายว่าระบบได้ส่งต่อสิทธิ์ให้ผู้เสนอราคาอันดับถัดไปแล้ว
                    db.Notifications.Add(new Notification
                    {
                        UserId = auction.SellerId,
                        Type = "payment_reassigned",
                        Title = $"ส่งต่อสิทธิ์ผู้ชนะอันดับถัดไป ({runnerUpOrder.OrderCode})",
                        Body = $"ผู้ชนะเดิมของ “{auction.Name}” เกินกำหนดชำระ ระบบเสนอสิทธิ์ให้ผู้เสนอราคาอันดับถัดไปที่ ฿{nextBidder.Amount:N0} แล้ว",
                        TargetView = "seller-orders",
                        ReferenceId = auction.Id,
                        IsRead = false,
                        CreatedAt = now
                    });
                }
                else
                {
                    // ไม่มีผู้เสนอราคาอันดับถัดไปเหลืออยู่แล้ว -> ล้างผู้ชนะเป็น null และแจ้งผู้ขายให้นำสินค้าลงประมูลใหม่
                    auction.WinnerId = null;
                    auction.Version++;

                    db.Notifications.Add(new Notification
                    {
                        UserId = auction.SellerId,
                        Type = "payment_expired",
                        Title = $"ผู้ชนะไม่ชำระเงินตามกำหนด: “{auction.Name}”",
                        Body = $"รายการ {order.OrderCode} ถูกยกเลิกและไม่มีผู้เสนอราคาอันดับถัดไป คุณสามารถนำสินค้าลงประมูลใหม่ได้",
                        TargetView = "seller-orders",
                        ReferenceId = auction.Id,
                        IsRead = false,
                        CreatedAt = now
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            return overdueOrders.Count;
        }
        finally
        {
            // ปล่อยล็อกเสมอเมื่อทำงานเสร็จ
            LifecycleLock.Release();
        }
    }

    // ------------------------------------------------------------------------
    // เมธอดช่วยตรวจสอบและสร้างห้องแชทอัตโนมัติระหว่างผู้ชนะประมูลกับผู้ขาย (พร้อมข้อความยินดีเริ่มต้น)
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ห้องแชทที่สร้างนี้จะปรากฏในหน้าข้อความของทั้งสองฝ่ายผ่าน GET /api/chat/conversations
    // ------------------------------------------------------------------------
    private async Task EnsureWinnerConversationAsync(AuctionItem auction, int winnerId, decimal winningAmount, DateTime now, CancellationToken ct)
    {
        // เช็กก่อนว่าทั้งคู่มีห้องแชทสำหรับสินค้าชิ้นนี้อยู่แล้วหรือไม่ (เพื่อไม่ให้ชน Unique Index)
        var hasConv = await db.Conversations.AnyAsync(
            c => c.AuctionItemId == auction.Id && c.BuyerId == winnerId && c.SellerId == auction.SellerId, ct);
        if (hasConv) return;

        // สร้างห้องแชทใหม่พร้อมข้อความแรกจากฝั่งผู้ขาย (ตั้ง SellerLastReadAt = now เพื่อให้ผู้ซื้อเห็นเป็นข้อความใหม่ 1 ข้อความ)
        db.Conversations.Add(new Conversation
        {
            AuctionItemId = auction.Id,
            BuyerId = winnerId,
            SellerId = auction.SellerId,
            SellerLastReadAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Messages =
            [
                new ChatMessage
                {
                    SenderId = auction.SellerId,
                    Body = $"ยินดีด้วยครับ คุณชนะการประมูล “{auction.Name}” ที่ราคา ฿{winningAmount:N0} กรุณาชำระเงินภายใน {PaymentDeadlineHours} ชม. แล้วสามารถนัดรับสินค้าทางช่องทางนี้ได้เลยครับ",
                    SentAt = now
                }
            ]
        });
    }
}

// ============================================================================
// บริการทำงานเบื้องหลัง (Hosted Background Service)
// หน้าที่: รันลูปอัตโนมัติทุก 15 วินาทีตลอดอายุการทำงานของเซิร์ฟเวอร์ เพื่อปิดประมูลที่หมดเวลา
//         และยกเลิก/โอนสิทธิ์คำสั่งซื้อที่เลยกำหนดชำระ 48 ชั่วโมง (ลงทะเบียนใน Program.cs)
// ============================================================================
public class AuctionClosingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AuctionClosingBackgroundService> logger) : BackgroundService
{
    // เมธอดหลักที่ ASP.NET Core Host เรียกทำงานอัตโนมัติเมื่อเปิดเซิร์ฟเวอร์และหยุดเมื่อได้รับ stoppingToken
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ตัวจับเวลารอบละ 15 วินาที (PeriodicTimer ไม่ซ้อนทับกันและประหยัดทรัพยากร)
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // สร้าง DI Scope ใหม่ในแต่ละรอบ เพราะ BackgroundService เป็น Singleton แต่ AppDbContext / AuctionLifecycleService เป็น Scoped
                using var scope = scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<AuctionLifecycleService>();

                // รอบงานที่ 1: ปิดรายการประมูลที่ครบเวลา EndAt และสร้างคำสั่งซื้อ/แจ้งเตือนให้ผู้ชนะ
                var closed = await lifecycle.CloseExpiredAuctionsAsync(stoppingToken);
                if (closed > 0)
                    logger.LogInformation("Closed {Count} expired auctions.", closed);

                // รอบงานที่ 2: ยกเลิกคำสั่งซื้อที่เกินกำหนดชำระ 48 ชม. และส่งต่อสิทธิ์ให้ผู้เสนอราคาอันดับถัดไป
                var expiredPay = await lifecycle.ProcessExpiredPaymentsAsync(stoppingToken);
                if (expiredPay > 0)
                    logger.LogInformation("Processed {Count} overdue unpaid orders.", expiredPay);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // บันทึก Log ข้อผิดพลาดโดยไม่ปล่อยให้ BackgroundService หยุดทำงาน (เพื่อให้รอบถัดไปยังทำงานต่อได้)
                logger.LogError(ex, "Error in auction lifecycle background task.");
            }

            try
            {
                // รอจนครบ 15 วินาทีเพื่อเริ่มรอบถัดไป (จะตื่นและยกเลิกทันทีเมื่อเซิร์ฟเวอร์สั่งปิด)
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // ออกจากลูปอย่างเรียบร้อยเมื่อแอปพลิเคชันกำลังปิดตัว (Graceful Shutdown)
                break;
            }
        }
    }
}
