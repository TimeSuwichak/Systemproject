using AppProject.Data;
using AppProject.Models;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Services;

public class AuctionLifecycleService(AppDbContext db)
{
    public const int PaymentDeadlineHours = 48;
    public const int AutoReleaseAfterDeliveredDays = 3;
    private static readonly SemaphoreSlim LifecycleLock = new(1, 1);

    public static bool CanReleaseEscrow(OrderPayment order, bool hasOpenDispute, DateTime utcNow)
    {
        if (order.PaymentMethod != PaymentMethods.PlatformEscrow) return false;
        if (order.PaymentStatus != PaymentStatuses.Paid) return false;
        if (order.PayoutStatus != PayoutStatuses.Pending) return false;
        if (hasOpenDispute) return false;

        if (order.MeetupStatus == MeetupStatuses.Completed)
            return true;

        if (order.MeetupStatus == MeetupStatuses.Delivered
            && order.DeliveredAt.HasValue
            && order.DeliveredAt.Value <= utcNow.AddDays(-AutoReleaseAfterDeliveredDays))
            return true;

        return false;
    }

    public async Task<FinanceSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await db.FinanceSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is not null) return settings;

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

    public async Task<(decimal discountPercent, string? reason, decimal discountAmount, decimal finalBuyerAmount, decimal feePercent, decimal feeAmount, decimal sellerNet)>
        CalculatePricingAsync(int buyerId, int categoryId, decimal winningPrice, int? excludeOrderId = null, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);

        // Only count already-PAID orders in the same category so winning 2 unpaid items never discounts both!
        var paidSameCategoryCount = await db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.BuyerId == buyerId
                && o.PaymentStatus == PaymentStatuses.Paid
                && o.AuctionItem != null
                && o.AuctionItem.CategoryId == categoryId
                && (!excludeOrderId.HasValue || o.Id != excludeOrderId.Value))
            .CountAsync(ct);

        var eligible = paidSameCategoryCount >= 1 && settings.SameCategoryDiscountPercent > 0;
        var discountPercent = eligible ? settings.SameCategoryDiscountPercent : 0m;
        var discountAmount = Math.Round(winningPrice * discountPercent / 100m, 2);
        var finalBuyerAmount = Math.Max(0m, winningPrice - discountAmount);
        var feePercent = settings.PlatformFeePercent;
        var feeAmount = Math.Round(finalBuyerAmount * feePercent / 100m, 2);
        var sellerNet = Math.Max(0m, finalBuyerAmount - feeAmount);
        var reason = eligible
            ? $"ส่วนลดซื้อหมวดเดียวกัน {discountPercent:0.##}% (ชำระแล้วในหมวดนี้ {paidSameCategoryCount} ชิ้น)"
            : null;

        return (discountPercent, reason, discountAmount, finalBuyerAmount, feePercent, feeAmount, sellerNet);
    }

    public async Task RecalculateUnpaidOrdersAsync(int? onlyBuyerId = null, CancellationToken ct = default)
    {
        var query = db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.PaymentStatus == PaymentStatuses.Unpaid && o.AuctionItem != null);

        if (onlyBuyerId.HasValue)
            query = query.Where(o => o.BuyerId == onlyBuyerId.Value);

        var unpaid = await query.ToListAsync(ct);
        if (unpaid.Count == 0) return;

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

    public async Task<int> CloseExpiredAuctionsAsync(CancellationToken ct = default)
    {
        await LifecycleLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var expired = await db.AuctionItems
                .Include(a => a.Bids)
                .Where(a => a.Status == AuctionStatuses.Approved && a.EndAt <= now)
                .ToListAsync(ct);

            if (expired.Count == 0) return 0;

            foreach (var auction in expired)
            {
                auction.Status = AuctionStatuses.Closed;
                auction.Version++;

                var highestBid = auction.Bids
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.CreatedAt)
                    .FirstOrDefault();

                if (highestBid is null)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = auction.SellerId,
                        Type = "auction_closed",
                        Title = $"ปิดประมูลแล้ว (ไม่มีผู้เสนอราคา): “{auction.Name}”",
                        Body = "สินค้าหมดเวลาประมูลโดยไม่มีผู้เสนอราคา คุณสามารถลงขายใหม่อีกครั้งได้",
                        TargetView = "sell",
                        ReferenceId = auction.Id,
                        IsRead = false,
                        CreatedAt = now
                    });
                    continue;
                }

                auction.WinnerId = highestBid.BidderId;
                auction.CurrentPrice = highestBid.Amount;

                var alreadyHasOrder = await db.OrderPayments.AnyAsync(
                    o => o.AuctionItemId == auction.Id && o.BuyerId == highestBid.BidderId, ct);
                if (alreadyHasOrder) continue;

                var pricing = await CalculatePricingAsync(highestBid.BidderId, auction.CategoryId, highestBid.Amount, null, ct);

                var order = new OrderPayment
                {
                    OrderCode = $"AUC-{auction.Id:D5}",
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
                    PaymentDueAt = now.AddHours(PaymentDeadlineHours),
                    MeetupStatus = MeetupStatuses.NotScheduled,
                    PayoutStatus = PayoutStatuses.Pending,
                    CreatedAt = now
                };
                db.OrderPayments.Add(order);

                await EnsureWinnerConversationAsync(auction, highestBid.BidderId, highestBid.Amount, now, ct);

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
            LifecycleLock.Release();
        }
    }

    public async Task<int> ProcessExpiredPaymentsAsync(CancellationToken ct = default)
    {
        await LifecycleLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var overdueOrders = await db.OrderPayments
                .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Bids)
                .Where(o => o.PaymentStatus == PaymentStatuses.Unpaid
                    && o.PaymentDueAt != null
                    && o.PaymentDueAt <= now)
                .ToListAsync(ct);

            if (overdueOrders.Count == 0) return 0;

            foreach (var order in overdueOrders)
            {
                order.PaymentStatus = PaymentStatuses.Cancelled;
                order.PayoutStatus = PayoutStatuses.Cancelled;
                order.FinanceNote = $"ยกเลิกอัตโนมัติ: ไม่ชำระเงินภายใน {PaymentDeadlineHours} ชม.";

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

                // Find buyers who already had an order on this auction (including current cancelled buyer)
                var excludedBuyerIds = await db.OrderPayments
                    .Where(o => o.AuctionItemId == auction.Id)
                    .Select(o => o.BuyerId)
                    .ToListAsync(ct);

                var nextBidder = auction.Bids
                    .Where(b => !excludedBuyerIds.Contains(b.BidderId))
                    .GroupBy(b => b.BidderId)
                    .Select(g => g.OrderByDescending(b => b.Amount).ThenBy(b => b.CreatedAt).First())
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.CreatedAt)
                    .FirstOrDefault();

                if (nextBidder is not null)
                {
                    var attempt = excludedBuyerIds.Count + 1;
                    var pricing = await CalculatePricingAsync(nextBidder.BidderId, auction.CategoryId, nextBidder.Amount, null, ct);

                    auction.WinnerId = nextBidder.BidderId;
                    auction.CurrentPrice = nextBidder.Amount;
                    auction.Version++;

                    var runnerUpOrder = new OrderPayment
                    {
                        OrderCode = $"AUC-{auction.Id:D5}-R{attempt}",
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

                    await EnsureWinnerConversationAsync(auction, nextBidder.BidderId, nextBidder.Amount, now, ct);

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
            LifecycleLock.Release();
        }
    }

    private async Task EnsureWinnerConversationAsync(AuctionItem auction, int winnerId, decimal winningAmount, DateTime now, CancellationToken ct)
    {
        var hasConv = await db.Conversations.AnyAsync(
            c => c.AuctionItemId == auction.Id && c.BuyerId == winnerId && c.SellerId == auction.SellerId, ct);
        if (hasConv) return;

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

public class AuctionClosingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AuctionClosingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<AuctionLifecycleService>();
                var closed = await lifecycle.CloseExpiredAuctionsAsync(stoppingToken);
                if (closed > 0)
                    logger.LogInformation("Closed {Count} expired auctions.", closed);

                var expiredPay = await lifecycle.ProcessExpiredPaymentsAsync(stoppingToken);
                if (expiredPay > 0)
                    logger.LogInformation("Processed {Count} overdue unpaid orders.", expiredPay);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error in auction lifecycle background task.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
