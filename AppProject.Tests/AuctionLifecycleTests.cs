// ============================================================================
// ไฟล์: AppProject.Tests/AuctionLifecycleTests.cs (ชุดทดสอบอัตโนมัติ Unit Tests ด้วย xUnit)
// หน้าที่: ทดสอบความถูกต้องของ Business Logic ฝั่งหลังบ้าน โดยใช้ฐานข้อมูล SQLite ในหน่วยความจำ (:memory:)
//         ครอบคลุม 5 กรณีหลัก:
//         1. กฎส่วนลดหมวดหมู่เดียวกัน (ป้องกันการใช้สิทธิ์ซ้ำเมื่อออเดอร์แรกยังไม่จ่ายเงิน)
//         2. การปิดประมูลที่หมดเวลา, การสร้างรหัสออเดอร์แบบไม่ซ้ำ (Idempotent), และการแจ้งเตือนทั้งผู้ซื้อ-ผู้ขาย
//         3. การยกเลิกออเดอร์ที่เลยกำหนดชำระเงิน และส่งต่อสิทธิ์ให้ผู้บิดอันดับรอง (Runner-up)
//         4. กฎความปลอดภัยก่อนฝ่ายการเงินโอนเงิน Escrow ให้ผู้ขาย (ตรวจข้อพิพาท + การยืนยันรับของ/ครบ 3 วัน)
//         5. การเข้ารหัสและตรวจสอบรหัสผ่านของ PasswordService
// เชื่อมต่อกับ: Services/AuctionLifecycleService.cs, Services/PasswordService.cs, Data/AppDbContext.cs
// ============================================================================

using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppProject.Tests;

public class AuctionLifecycleTests : IDisposable
{
    // เชื่อมต่อ SQLite แบบ In-Memory แยกอิสระต่อ 1 เทสต์ เพื่อไม่ให้กระทบไฟล์ auction.db จริง
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly AuctionLifecycleService _service;

    // Constructor: รันก่อนทุกเทสต์เพื่อสร้างฐานข้อมูลเปล่า รัน Migration และสร้าง Service
    public AuctionLifecycleTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new AuctionLifecycleService(_db);
    }

    // ปิดการเชื่อมต่อและคืนทรัพยากรเมื่อจบแต่ละเทสต์
    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ตัวช่วยสร้างข้อมูลตั้งต้นสำหรับเทสต์: ผู้ขาย 1 คน, ผู้ซื้อ 2 คน, หมวดหมู่ 1 หมวด, และตั้งค่าการเงิน (ลด 10%, ค่าธรรมเนียม 5%)
    private async Task<(User seller, User buyer1, User buyer2, Category cat)> SeedBaseAsync()
    {
        var seller = new User { DisplayName = "Seller", Email = "s@t.th", PasswordHash = "x", Role = Roles.Seller, SellerStatus = SellerStatuses.Approved };
        var buyer1 = new User { DisplayName = "Buyer1", Email = "b1@t.th", PasswordHash = "x", Role = Roles.Buyer };
        var buyer2 = new User { DisplayName = "Buyer2", Email = "b2@t.th", PasswordHash = "x", Role = Roles.Buyer };
        var cat = new Category { Name = "เซรามิก", Symbol = "◌", IsActive = true };

        _db.Users.AddRange(seller, buyer1, buyer2);
        _db.Categories.Add(cat);
        _db.FinanceSettings.Add(new FinanceSettings
        {
            Id = 1,
            SameCategoryDiscountPercent = 10,
            PlatformFeePercent = 5,
            GlobalMinBidIncrement = 50
        });
        await _db.SaveChangesAsync();
        return (seller, buyer1, buyer2, cat);
    }

    // เทสต์ที่ 1: ตรวจสอบว่าผู้ซื้อจะยังไม่ได้รับส่วนลดหมวดหมู่เดียวกัน 10% หากออเดอร์แรกยังไม่ได้ชำระเงิน (Unpaid)
    // และจะได้รับส่วนลดทันทีเมื่อออเดอร์แรกชำระเงินเรียบร้อยแล้ว (Paid)
    [Fact]
    public async Task SameCategoryDiscount_DoesNotDoubleDip_WhenBothOrdersUnpaid()
    {
        var (seller, buyer1, _, cat) = await SeedBaseAsync();
        var now = DateTime.UtcNow;

        var item1 = new AuctionItem { Name = "Item1", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 1000, CurrentPrice = 1000, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-5), Status = AuctionStatuses.Closed };
        var item2 = new AuctionItem { Name = "Item2", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 2000, CurrentPrice = 2000, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-5), Status = AuctionStatuses.Closed };
        _db.AuctionItems.AddRange(item1, item2);
        await _db.SaveChangesAsync();

        // สร้างออเดอร์แรกในสถานะยังไม่ชำระเงิน (UNPAID)
        _db.OrderPayments.Add(new OrderPayment
        {
            OrderCode = "AUC-00001",
            AuctionItemId = item1.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            WinningPrice = 1000,
            FinalBuyerAmount = 1000,
            PaymentStatus = PaymentStatuses.Unpaid
        });
        await _db.SaveChangesAsync();

        // คำนวณราคาชิ้นที่ 2 ขณะที่ชิ้นแรกยัง UNPAID -> ต้องได้ส่วนลด 0%
        var pricingBeforePay = await _service.CalculatePricingAsync(buyer1.Id, cat.Id, 2000);
        Assert.Equal(0m, pricingBeforePay.discountPercent);
        Assert.Equal(0m, pricingBeforePay.discountAmount);
        Assert.Equal(2000m, pricingBeforePay.finalBuyerAmount);
        Assert.Equal(100m, pricingBeforePay.feeAmount);   // ค่าธรรมเนียม 5% ของ 2000 = 100
        Assert.Equal(1900m, pricingBeforePay.sellerNet);

        // เมื่อชิ้นแรกชำระเงินแล้ว (PAID) -> ชิ้นที่ 2 ในหมวดเดียวกันต้องได้ส่วนลด 10%
        var firstOrder = await _db.OrderPayments.FirstAsync();
        firstOrder.PaymentStatus = PaymentStatuses.Paid;
        await _db.SaveChangesAsync();

        var pricingAfterPay = await _service.CalculatePricingAsync(buyer1.Id, cat.Id, 2000);
        Assert.Equal(10m, pricingAfterPay.discountPercent);
        Assert.Equal(200m, pricingAfterPay.discountAmount);
        Assert.Equal(1800m, pricingAfterPay.finalBuyerAmount);
        Assert.Equal(90m, pricingAfterPay.feeAmount);    // ค่าธรรมเนียม 5% ของยอดหลังหักส่วนลด (1800) = 90
        Assert.Equal(1710m, pricingAfterPay.sellerNet);
    }

    // เทสต์ที่ 2: ปิดประมูลที่หมดเวลา ต้องสร้างรหัสออเดอร์ AUC-xxxxx, รันซ้ำแล้วไม่สร้างออเดอร์เบิ้ล (Idempotent),
    // และสร้างการแจ้งเตือนให้ทั้งผู้ชนะและผู้ขาย (รวมถึงรายการที่ไม่มีผู้ประมูล)
    [Fact]
    public async Task CloseExpiredAuctions_CreatesDeterministicOrderCode_IsIdempotent_AndNotifiesBothParties()
    {
        var (seller, buyer1, _, cat) = await SeedBaseAsync();
        var now = DateTime.UtcNow;

        var itemWithBid = new AuctionItem { Name = "Won Item", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 500, CurrentPrice = 700, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-1), Status = AuctionStatuses.Approved };
        var itemNoBid = new AuctionItem { Name = "Unsold Item", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 500, CurrentPrice = 500, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-1), Status = AuctionStatuses.Approved };
        _db.AuctionItems.AddRange(itemWithBid, itemNoBid);
        await _db.SaveChangesAsync();

        _db.Bids.Add(new Bid { AuctionItemId = itemWithBid.Id, BidderId = buyer1.Id, Amount = 700, CreatedAt = now.AddMinutes(-10) });
        await _db.SaveChangesAsync();

        var closed1 = await _service.CloseExpiredAuctionsAsync();
        var closed2 = await _service.CloseExpiredAuctionsAsync(); // เรียกซ้ำรอบ 2 ต้องได้ 0 (ไม่ทำงานซ้ำ)

        Assert.Equal(2, closed1);
        Assert.Equal(0, closed2);

        // ตรวจว่ามีออเดอร์เดียวสำหรับสินค้าที่มีคนบิด และมีรหัสออเดอร์ + กำหนดเวลาชำระเงินครบถ้วน
        var orders = await _db.OrderPayments.ToListAsync();
        Assert.Single(orders);
        Assert.Equal($"AUC-{itemWithBid.Id:D5}", orders[0].OrderCode);
        Assert.NotNull(orders[0].PaymentDueAt);

        // ตรวจการแจ้งเตือน: ผู้ชนะได้รับ auction_won และผู้ขายได้รับทั้ง auction_sold และ auction_closed
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == buyer1.Id && n.Type == "auction_won"));
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == seller.Id && n.Type == "auction_sold"));
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == seller.Id && n.Type == "auction_closed"));
    }

    // เทสต์ที่ 3: เมื่อผู้ชนะอันดับ 1 ไม่จ่ายเงินเกินกำหนด (Overdue) ระบบต้องยกเลิกออเดอร์เดิม
    // และสร้างออเดอร์ใหม่รหัสลงท้าย -R2 เพื่อส่งต่อสิทธิ์ให้ผู้ประมูลอันดับ 2 (Runner-up)
    [Fact]
    public async Task ProcessExpiredPayments_CancelsOverdueOrder_AndOffersToRunnerUp()
    {
        var (seller, buyer1, buyer2, cat) = await SeedBaseAsync();
        var now = DateTime.UtcNow;

        var item = new AuctionItem { Name = "Antique Vase", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 1000, CurrentPrice = 1500, StartAt = now.AddDays(-3), EndAt = now.AddDays(-2), Status = AuctionStatuses.Closed, WinnerId = buyer1.Id };
        _db.AuctionItems.Add(item);
        await _db.SaveChangesAsync();

        // ประวัติการบิด: buyer2 บิด 1300 (รองชนะเลิศ), buyer1 บิด 1500 (ผู้ชนะอันดับ 1)
        _db.Bids.AddRange(
            new Bid { AuctionItemId = item.Id, BidderId = buyer2.Id, Amount = 1300, CreatedAt = now.AddDays(-2).AddHours(-2) },
            new Bid { AuctionItemId = item.Id, BidderId = buyer1.Id, Amount = 1500, CreatedAt = now.AddDays(-2).AddHours(-1) }
        );

        // ออเดอร์ของ buyer1 ที่เลยกำหนดชำระเงินมาแล้ว 1 ชั่วโมง
        _db.OrderPayments.Add(new OrderPayment
        {
            OrderCode = $"AUC-{item.Id:D5}",
            AuctionItemId = item.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            WinningPrice = 1500,
            FinalBuyerAmount = 1500,
            PaymentStatus = PaymentStatuses.Unpaid,
            PaymentDueAt = now.AddHours(-1), // เกินกำหนดชำระแล้ว
            PayoutStatus = PayoutStatuses.Pending
        });
        await _db.SaveChangesAsync();

        // รันตรวจออเดอร์หมดอายุ -> ต้องจัดการได้ 1 รายการ
        var processed = await _service.ProcessExpiredPaymentsAsync();
        Assert.Equal(1, processed);

        // ตรวจว่าออเดอร์เดิมถูก Cancelled และเกิดออเดอร์ใหม่ให้ buyer2 ในราคา 1300 รหัส -R2
        var orders = await _db.OrderPayments.OrderBy(o => o.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(PaymentStatuses.Cancelled, orders[0].PaymentStatus);
        Assert.Equal(buyer2.Id, orders[1].BuyerId);
        Assert.Equal(1300m, orders[1].WinningPrice);
        Assert.Equal(PaymentStatuses.Unpaid, orders[1].PaymentStatus);
        Assert.Equal($"AUC-{item.Id:D5}-R2", orders[1].OrderCode);

        // ตรวจว่าผู้ชนะของสินค้าถูกเปลี่ยนเป็น buyer2 เรียบร้อย
        var updatedItem = await _db.AuctionItems.FirstAsync(a => a.Id == item.Id);
        Assert.Equal(buyer2.Id, updatedItem.WinnerId);
    }

    // เทสต์ที่ 4: ตรวจสอบเงื่อนไขก่อนโอนเงินจากบัญชีกลาง (Escrow) ให้ผู้ขาย (CanReleaseEscrow)
    [Fact]
    public void CanReleaseEscrow_EnforcesDisputeAndBuyerConfirmationRules()
    {
        var now = DateTime.UtcNow;
        var baseOrder = new OrderPayment
        {
            PaymentMethod = PaymentMethods.PlatformEscrow,
            PaymentStatus = PaymentStatuses.Paid,
            PayoutStatus = PayoutStatuses.Pending,
            MeetupStatus = MeetupStatuses.Delivered,
            DeliveredAt = now.AddHours(-6) // ผู้ขายเพิ่งส่งมอบไป 6 ชม. (ยังไม่ครบ 3 วัน)
        };

        // 1. ผู้ขายส่งมอบแค่ 6 ชม. และผู้ซื้อยังไม่กดยืนยัน -> ไม่อนุญาตให้โอนออก
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 2. ผู้ขายส่งมอบเกิน 3 วัน (4 วัน) และไม่มีข้อพิพาท -> อนุญาตให้โอนออกได้
        baseOrder.DeliveredAt = now.AddDays(-4);
        Assert.True(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 3. แม้ส่งมอบครบ 4 วันแล้ว แต่มีข้อพิพาทเปิดอยู่ -> ต้องบล็อกห้ามโอน
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: true, now));

        // 4. ผู้ซื้อกดยืนยันรับของเรียบร้อยแล้ว (Completed) และไม่มีข้อพิพาท -> อนุญาตทันที
        baseOrder.MeetupStatus = MeetupStatuses.Completed;
        baseOrder.DeliveredAt = now.AddMinutes(-5);
        Assert.True(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 5. ผู้ซื้อยืนยันแล้ว แต่ยังมีข้อพิพาทค้างอยู่ -> ต้องบล็อกห้ามโอน
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: true, now));

        // 6. การโอนตรงให้ผู้ขาย (DirectToSeller) ไม่ผ่านบัญชีกลาง -> ห้ามกดโอน Escrow
        baseOrder.PaymentMethod = PaymentMethods.DirectToSeller;
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));
    }

    // เทสต์ที่ 5: ตรวจสอบว่า PasswordService สุ่ม Salt ไม่ซ้ำกันทุกครั้งและยืนยันรหัสผ่านได้ถูกต้อง
    [Fact]
    public void PasswordService_HashesWithUniqueSaltAndVerifies()
    {
        var h1 = PasswordService.Hash("secret123");
        var h2 = PasswordService.Hash("secret123");

        Assert.NotEqual(h1, h2); // รหัสผ่านเดียวกันแต่ Salt ต่างกัน -> แฮชต้องไม่เหมือนกัน
        Assert.True(PasswordService.Verify("secret123", h1));
        Assert.True(PasswordService.Verify("secret123", h2));
        Assert.False(PasswordService.Verify("wrongpass", h1));
    }
}
