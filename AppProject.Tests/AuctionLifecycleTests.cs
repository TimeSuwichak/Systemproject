using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppProject.Tests;

public class AuctionLifecycleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly AuctionLifecycleService _service;

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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

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

    [Fact]
    public async Task SameCategoryDiscount_DoesNotDoubleDip_WhenBothOrdersUnpaid()
    {
        var (seller, buyer1, _, cat) = await SeedBaseAsync();
        var now = DateTime.UtcNow;

        var item1 = new AuctionItem { Name = "Item1", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 1000, CurrentPrice = 1000, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-5), Status = AuctionStatuses.Closed };
        var item2 = new AuctionItem { Name = "Item2", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 2000, CurrentPrice = 2000, StartAt = now.AddHours(-2), EndAt = now.AddMinutes(-5), Status = AuctionStatuses.Closed };
        _db.AuctionItems.AddRange(item1, item2);
        await _db.SaveChangesAsync();

        // First order is still UNPAID
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

        // Second item pricing while 1st is UNPAID -> 0% discount!
        var pricingBeforePay = await _service.CalculatePricingAsync(buyer1.Id, cat.Id, 2000);
        Assert.Equal(0m, pricingBeforePay.discountPercent);
        Assert.Equal(0m, pricingBeforePay.discountAmount);
        Assert.Equal(2000m, pricingBeforePay.finalBuyerAmount);
        Assert.Equal(100m, pricingBeforePay.feeAmount);   // 5% of 2000
        Assert.Equal(1900m, pricingBeforePay.sellerNet);

        // Once 1st order is PAID -> 2nd item unlocks 10% same-category discount!
        var firstOrder = await _db.OrderPayments.FirstAsync();
        firstOrder.PaymentStatus = PaymentStatuses.Paid;
        await _db.SaveChangesAsync();

        var pricingAfterPay = await _service.CalculatePricingAsync(buyer1.Id, cat.Id, 2000);
        Assert.Equal(10m, pricingAfterPay.discountPercent);
        Assert.Equal(200m, pricingAfterPay.discountAmount);
        Assert.Equal(1800m, pricingAfterPay.finalBuyerAmount);
        Assert.Equal(90m, pricingAfterPay.feeAmount);    // 5% of 1800
        Assert.Equal(1710m, pricingAfterPay.sellerNet);
    }

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
        var closed2 = await _service.CloseExpiredAuctionsAsync(); // Idempotent second call

        Assert.Equal(2, closed1);
        Assert.Equal(0, closed2);

        var orders = await _db.OrderPayments.ToListAsync();
        Assert.Single(orders);
        Assert.Equal($"AUC-{itemWithBid.Id:D5}", orders[0].OrderCode);
        Assert.NotNull(orders[0].PaymentDueAt);

        // Winner notified + Seller notified for BOTH sold item and unsold item
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == buyer1.Id && n.Type == "auction_won"));
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == seller.Id && n.Type == "auction_sold"));
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == seller.Id && n.Type == "auction_closed"));
    }

    [Fact]
    public async Task ProcessExpiredPayments_CancelsOverdueOrder_AndOffersToRunnerUp()
    {
        var (seller, buyer1, buyer2, cat) = await SeedBaseAsync();
        var now = DateTime.UtcNow;

        var item = new AuctionItem { Name = "Antique Vase", CategoryId = cat.Id, SellerId = seller.Id, Condition = "Good", Description = "Desc", StartPrice = 1000, CurrentPrice = 1500, StartAt = now.AddDays(-3), EndAt = now.AddDays(-2), Status = AuctionStatuses.Closed, WinnerId = buyer1.Id };
        _db.AuctionItems.Add(item);
        await _db.SaveChangesAsync();

        _db.Bids.AddRange(
            new Bid { AuctionItemId = item.Id, BidderId = buyer2.Id, Amount = 1300, CreatedAt = now.AddDays(-2).AddHours(-2) },
            new Bid { AuctionItemId = item.Id, BidderId = buyer1.Id, Amount = 1500, CreatedAt = now.AddDays(-2).AddHours(-1) }
        );

        _db.OrderPayments.Add(new OrderPayment
        {
            OrderCode = $"AUC-{item.Id:D5}",
            AuctionItemId = item.Id,
            BuyerId = buyer1.Id,
            SellerId = seller.Id,
            WinningPrice = 1500,
            FinalBuyerAmount = 1500,
            PaymentStatus = PaymentStatuses.Unpaid,
            PaymentDueAt = now.AddHours(-1), // Overdue!
            PayoutStatus = PayoutStatuses.Pending
        });
        await _db.SaveChangesAsync();

        var processed = await _service.ProcessExpiredPaymentsAsync();
        Assert.Equal(1, processed);

        var orders = await _db.OrderPayments.OrderBy(o => o.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(PaymentStatuses.Cancelled, orders[0].PaymentStatus);
        Assert.Equal(buyer2.Id, orders[1].BuyerId);
        Assert.Equal(1300m, orders[1].WinningPrice);
        Assert.Equal(PaymentStatuses.Unpaid, orders[1].PaymentStatus);
        Assert.Equal($"AUC-{item.Id:D5}-R2", orders[1].OrderCode);

        var updatedItem = await _db.AuctionItems.FirstAsync(a => a.Id == item.Id);
        Assert.Equal(buyer2.Id, updatedItem.WinnerId);
    }

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
            DeliveredAt = now.AddHours(-6) // Only 6 hours ago (< 3 days)
        };

        // 1. Seller marked delivered only 6h ago without buyer confirming -> BLOCKED
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 2. Seller marked delivered 4 days ago, no dispute -> ALLOWED
        baseOrder.DeliveredAt = now.AddDays(-4);
        Assert.True(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 3. Even if delivered 4 days ago, if OPEN DISPUTE exists -> BLOCKED
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: true, now));

        // 4. Buyer confirmed Completed, no dispute -> ALLOWED
        baseOrder.MeetupStatus = MeetupStatuses.Completed;
        baseOrder.DeliveredAt = now.AddMinutes(-5);
        Assert.True(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));

        // 5. Buyer confirmed Completed, but OPEN DISPUTE exists -> BLOCKED
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: true, now));

        // 6. DirectToSeller -> NEVER released via Escrow
        baseOrder.PaymentMethod = PaymentMethods.DirectToSeller;
        Assert.False(AuctionLifecycleService.CanReleaseEscrow(baseOrder, hasOpenDispute: false, now));
    }

    [Fact]
    public void PasswordService_HashesWithUniqueSaltAndVerifies()
    {
        var h1 = PasswordService.Hash("secret123");
        var h2 = PasswordService.Hash("secret123");

        Assert.NotEqual(h1, h2);
        Assert.True(PasswordService.Verify("secret123", h1));
        Assert.True(PasswordService.Verify("secret123", h2));
        Assert.False(PasswordService.Verify("wrongpass", h1));
    }
}
