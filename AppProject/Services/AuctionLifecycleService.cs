using AppProject.Data;
using AppProject.Models;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Services;

public class AuctionLifecycleService(AppDbContext db)
{
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

        // Eligible for same-category discount if buyer has another non-refunded order in the same category
        var sameCategoryCount = await db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.BuyerId == buyerId
                && o.PaymentStatus != PaymentStatuses.Refunded
                && o.AuctionItem != null
                && o.AuctionItem.CategoryId == categoryId
                && (!excludeOrderId.HasValue || o.Id != excludeOrderId.Value))
            .CountAsync(ct);

        var eligible = sameCategoryCount >= 1 && settings.SameCategoryDiscountPercent > 0;
        var discountPercent = eligible ? settings.SameCategoryDiscountPercent : 0m;
        var discountAmount = Math.Round(winningPrice * discountPercent / 100m, 2);
        var finalBuyerAmount = Math.Max(0m, winningPrice - discountAmount);
        var feePercent = settings.PlatformFeePercent;
        var feeAmount = Math.Round(finalBuyerAmount * feePercent / 100m, 2);
        var sellerNet = Math.Max(0m, finalBuyerAmount - feeAmount);
        var reason = eligible
            ? $"ส่วนลดซื้อหมวดเดียวกัน {discountPercent:0.##}% (มีรายการในหมวดนี้แล้ว {sameCategoryCount} ชิ้น)"
            : null;

        return (discountPercent, reason, discountAmount, finalBuyerAmount, feePercent, feeAmount, sellerNet);
    }

    public async Task RecalculateUnpaidOrdersAsync(CancellationToken ct = default)
    {
        var unpaid = await db.OrderPayments
            .Include(o => o.AuctionItem)
            .Where(o => o.PaymentStatus == PaymentStatuses.Unpaid && o.AuctionItem != null)
            .ToListAsync(ct);

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

        if (unpaid.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    public async Task<int> CloseExpiredAuctionsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await db.AuctionItems
            .Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatuses.Approved && a.EndAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        var existingCount = await db.OrderPayments.CountAsync(ct);

        foreach (var auction in expired)
        {
            auction.Status = AuctionStatuses.Closed;
            var highestBid = auction.Bids
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefault();

            if (highestBid is null) continue;

            auction.WinnerId = highestBid.BidderId;
            auction.CurrentPrice = highestBid.Amount;

            var alreadyHasOrder = await db.OrderPayments.AnyAsync(o => o.AuctionItemId == auction.Id, ct);
            if (alreadyHasOrder) continue;

            var pricing = await CalculatePricingAsync(highestBid.BidderId, auction.CategoryId, highestBid.Amount, null, ct);
            existingCount++;

            var order = new OrderPayment
            {
                OrderCode = $"AUC-{existingCount:D4}",
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
                PaymentStatus = PaymentStatuses.Unpaid,
                MeetupStatus = MeetupStatuses.NotScheduled,
                PayoutStatus = PayoutStatuses.Pending,
                CreatedAt = now
            };
            db.OrderPayments.Add(order);

            // Ensure conversation exists between winner and seller
            var hasConv = await db.Conversations.AnyAsync(
                c => c.AuctionItemId == auction.Id && c.BuyerId == highestBid.BidderId && c.SellerId == auction.SellerId, ct);
            if (!hasConv)
            {
                db.Conversations.Add(new Conversation
                {
                    AuctionItemId = auction.Id,
                    BuyerId = highestBid.BidderId,
                    SellerId = auction.SellerId,
                    SellerLastReadAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Messages =
                    [
                        new ChatMessage
                        {
                            SenderId = auction.SellerId,
                            Body = $"ยินดีด้วยครับ คุณชนะการประมูล “{auction.Name}” ที่ราคา ฿{highestBid.Amount:N0} หลังชำระเงินแล้วสามารถนัดรับสินค้าทางช่องทางนี้ได้เลยครับ",
                            SentAt = now
                        }
                    ]
                });
            }

            db.Notifications.Add(new Notification
            {
                UserId = highestBid.BidderId,
                Type = "auction_won",
                Title = $"ยินดีด้วย! คุณชนะประมูล “{auction.Name}”",
                Body = $"ราคาปิด ฿{highestBid.Amount:N0} ยอดสุทธิ ฿{pricing.finalBuyerAmount:N0} ไปที่หน้าชำระเงินเพื่อดำเนินการต่อ",
                TargetView = "payments",
                ReferenceId = auction.Id,
                IsRead = false,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        return expired.Count;
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
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error while closing expired auctions.");
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
