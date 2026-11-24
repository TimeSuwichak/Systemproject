using System.Text;
using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Authorize(Roles = Roles.Finance)]
[Route("api/finance")]
public class FinanceController(AppDbContext db, AuctionLifecycleService lifecycle) : ApiBaseController(db)
{
    public record FinanceActionRequest(string? Note);
    public record VerifyEscrowPaymentRequest(bool Approve, string? Reason);
    public record UpdateSettingsRequest(
        decimal SameCategoryDiscountPercent,
        decimal PlatformFeePercent,
        decimal GlobalMinBidIncrement,
        string? PlatformPromptPayId,
        string? PlatformAccountName);

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        await lifecycle.CloseExpiredAuctionsAsync();
        await lifecycle.RecalculateUnpaidOrdersAsync();

        var settings = await lifecycle.GetSettingsAsync();
        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var disputesByOrder = await Db.Disputes
            .Where(d => d.OrderPaymentId != null)
            .GroupBy(d => d.OrderPaymentId!.Value)
            .Select(g => new { OrderId = g.Key, HasOpen = g.Any(x => x.Status != DisputeStatuses.Resolved), LatestStatus = g.OrderByDescending(x => x.CreatedAt).First().Status })
            .ToDictionaryAsync(x => x.OrderId);

        var paidOrders = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid).ToList();
        var escrowPaidOrders = paidOrders.Where(o => o.PaymentMethod == PaymentMethods.PlatformEscrow).ToList();

        return Ok(new
        {
            settings = new
            {
                settings.SameCategoryDiscountPercent,
                settings.PlatformFeePercent,
                settings.GlobalMinBidIncrement,
                settings.PlatformPromptPayId,
                settings.PlatformAccountName,
                settings.UpdatedAt
            },
            summary = new
            {
                totalOrders = orders.Count,
                paidOrdersCount = paidOrders.Count,
                unpaidOrdersCount = orders.Count(o => o.PaymentStatus == PaymentStatuses.Unpaid),
                pendingVerifyCount = orders.Count(o => o.PaymentStatus == PaymentStatuses.PendingVerification && o.PaymentMethod == PaymentMethods.PlatformEscrow),
                grossSales = paidOrders.Sum(o => o.WinningPrice),
                totalDiscounts = paidOrders.Sum(o => o.DiscountAmount),
                totalBuyerPaid = paidOrders.Sum(o => o.FinalBuyerAmount),
                totalPlatformFee = escrowPaidOrders.Sum(o => o.PlatformFeeAmount),
                totalReleased = orders.Where(o => o.PayoutStatus == PayoutStatuses.Released).Sum(o => o.SellerNetAmount),
                pendingRelease = escrowPaidOrders.Where(o => o.PayoutStatus == PayoutStatuses.Pending).Sum(o => o.SellerNetAmount),
                totalRefunded = orders.Where(o => o.PaymentStatus == PaymentStatuses.Refunded).Sum(o => o.FinalBuyerAmount)
            },
            orders = orders.Select(o =>
            {
                disputesByOrder.TryGetValue(o.Id, out var disp);
                var isEscrow = o.PaymentMethod == PaymentMethods.PlatformEscrow;
                return new
                {
                    o.Id,
                    o.OrderCode,
                    o.AuctionItemId,
                    itemName = o.AuctionItem?.Name ?? "",
                    categoryName = o.AuctionItem?.Category?.Name ?? "",
                    imageUrl = o.AuctionItem?.ImageUrl ?? "",
                    buyerName = o.Buyer?.DisplayName ?? "",
                    buyerEmail = o.Buyer?.Email ?? "",
                    sellerName = o.Seller?.ShopName ?? o.Seller?.DisplayName ?? "",
                    sellerBank = o.Seller?.BankAccount ?? "-",
                    o.WinningPrice,
                    o.DiscountPercent,
                    o.DiscountReason,
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
                    o.PaidAt,
                    o.MeetupStatus,
                    o.MeetupLocation,
                    o.MeetupAt,
                    o.DeliveredAt,
                    o.ReceivedAt,
                    o.PayoutStatus,
                    o.PayoutReleasedAt,
                    o.RefundedAt,
                    o.FinanceNote,
                    hasOpenDispute = disp?.HasOpen ?? false,
                    disputeStatus = disp?.LatestStatus,
                    canVerifyEscrowPayment = isEscrow && o.PaymentStatus == PaymentStatuses.PendingVerification,
                    canRelease = isEscrow
                        && o.PaymentStatus == PaymentStatuses.Paid
                        && o.PayoutStatus == PayoutStatuses.Pending
                        && (o.MeetupStatus == MeetupStatuses.Completed || o.MeetupStatus == MeetupStatuses.Delivered),
                    canRefund = isEscrow
                        && o.PaymentStatus == PaymentStatuses.Paid
                        && o.PayoutStatus == PayoutStatuses.Pending,
                    o.CreatedAt
                };
            })
        });
    }

    [HttpPost("orders/{orderId:int}/verify-payment")]
    public async Task<IActionResult> VerifyEscrowPayment(int orderId, [FromBody] VerifyEscrowPaymentRequest req)
    {
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้เป็นการชำระตรงกับผู้ขาย ต้องให้ผู้ขายเป็นผู้ยืนยัน" });
        if (order.PaymentStatus != PaymentStatuses.PendingVerification)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอตรวจสลิป" });

        var now = DateTime.UtcNow;
        if (req.Approve)
        {
            order.PaymentStatus = PaymentStatuses.Paid;
            order.PaidAt = now;
            order.PaymentRejectReason = null;

            Db.Notifications.Add(new Notification
            {
                UserId = order.BuyerId,
                Type = "payment_verified",
                Title = $"ฝ่ายการเงินยืนยันสลิปแล้ว ({order.OrderCode})",
                Body = $"รับชำระยอด ฿{order.FinalBuyerAmount:N0} สำหรับ “{order.AuctionItem?.Name}” เรียบร้อย รอผู้ขายกำหนดนัดรับได้เลย",
                TargetView = "payments",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });
            Db.Notifications.Add(new Notification
            {
                UserId = order.SellerId,
                Type = "payment_verified",
                Title = $"ผู้ซื้อชำระเงินเข้าระบบกลางแล้ว ({order.OrderCode})",
                Body = $"ฝ่ายการเงินยืนยันยอดสำหรับ “{order.AuctionItem?.Name}” แล้ว สามารถกำหนดนัดรับสินค้าได้เลย",
                TargetView = "seller-orders",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธสลิป" });

            order.PaymentStatus = PaymentStatuses.Unpaid;
            order.PaymentRejectReason = req.Reason.Trim();

            Db.Notifications.Add(new Notification
            {
                UserId = order.BuyerId,
                Type = "payment_rejected",
                Title = $"สลิปไม่ผ่านการตรวจสอบ ({order.OrderCode})",
                Body = $"เหตุผล: {order.PaymentRejectReason} — กรุณาอัปโหลดสลิปใหม่อีกครั้ง",
                TargetView = "payments",
                ReferenceId = order.Id,
                IsRead = false,
                CreatedAt = now
            });
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = req.Approve ? $"ยืนยันรับชำระเงิน {order.OrderCode} แล้ว" : $"ปฏิเสธสลิป {order.OrderCode} พร้อมส่งเหตุผลให้ผู้ซื้อแล้ว" });
    }

    [HttpPost("orders/{orderId:int}/release")]
    public async Task<IActionResult> ReleasePayout(int orderId, [FromBody] FinanceActionRequest? req)
    {
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้ผู้ขายรับเงินตรงจากผู้ซื้อแล้ว ไม่ต้องปล่อยเงินผ่านระบบกลาง" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ไม่สามารถปล่อยเงินได้ เนื่องจากรายการนี้ยังไม่ยืนยันการชำระเงินหรือถูกคืนเงินแล้ว" });
        if (order.PayoutStatus != PayoutStatuses.Pending)
            return BadRequest(new { message = "รายการนี้ดำเนินการด้านการเงินไปแล้ว" });
        if (order.MeetupStatus != MeetupStatuses.Completed && order.MeetupStatus != MeetupStatuses.Delivered)
            return BadRequest(new { message = "ต้องรอให้มีการยืนยันส่งมอบ/รับของก่อนจึงจะปล่อยเงินให้ผู้ขายได้" });

        var now = DateTime.UtcNow;
        order.PayoutStatus = PayoutStatuses.Released;
        order.PayoutReleasedAt = now;
        order.FinanceNote = string.IsNullOrWhiteSpace(req?.Note)
            ? "โอนเงินสุทธิให้ผู้ขายเรียบร้อยแล้ว"
            : req!.Note!.Trim();

        Db.Notifications.Add(new Notification
        {
            UserId = order.SellerId,
            Type = "payout_released",
            Title = $"ฝ่ายการเงินปล่อยเงินให้คุณแล้ว ({order.OrderCode})",
            Body = $"โอนยอดสุทธิ ฿{order.SellerNetAmount:N2} สำหรับ “{order.AuctionItem?.Name}” เรียบร้อย",
            TargetView = "seller-orders",
            ReferenceId = order.Id,
            IsRead = false,
            CreatedAt = now
        });

        await Db.SaveChangesAsync();
        return Ok(new { message = $"ปล่อยเงิน ฿{order.SellerNetAmount:N2} ให้ผู้ขาย ({order.OrderCode}) เรียบร้อยแล้ว" });
    }

    [HttpPost("orders/{orderId:int}/refund")]
    public async Task<IActionResult> RefundOrder(int orderId, [FromBody] FinanceActionRequest? req)
    {
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้ชำระตรงกับผู้ขาย ไม่มีเงินพักอยู่ในระบบกลาง" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "รายการนี้ยังไม่ได้ชำระเงินหรือคืนเงินไปแล้ว" });
        if (order.PayoutStatus == PayoutStatuses.Released)
            return BadRequest(new { message = "ไม่สามารถคืนเงินได้ เนื่องจากปล่อยเงินให้ผู้ขายไปแล้ว" });

        var now = DateTime.UtcNow;
        order.PaymentStatus = PaymentStatuses.Refunded;
        order.PayoutStatus = PayoutStatuses.Refunded;
        order.RefundedAt = now;
        order.FinanceNote = string.IsNullOrWhiteSpace(req?.Note)
            ? "คืนเงินเต็มจำนวนให้ผู้ประมูลเรียบร้อยแล้ว"
            : req!.Note!.Trim();

        Db.Notifications.Add(new Notification
        {
            UserId = order.BuyerId,
            Type = "refunded",
            Title = $"ฝ่ายการเงินคืนเงินให้คุณแล้ว ({order.OrderCode})",
            Body = $"คืนเงินยอด ฿{order.FinalBuyerAmount:N2} สำหรับ “{order.AuctionItem?.Name}” — {order.FinanceNote}",
            TargetView = "payments",
            ReferenceId = order.Id,
            IsRead = false,
            CreatedAt = now
        });

        await Db.SaveChangesAsync();
        return Ok(new { message = $"คืนเงิน ฿{order.FinalBuyerAmount:N2} ให้ผู้ซื้อ ({order.OrderCode}) เรียบร้อยแล้ว" });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        if (req.SameCategoryDiscountPercent < 0 || req.SameCategoryDiscountPercent > 50)
            return BadRequest(new { message = "ส่วนลดหมวดเดียวกันต้องอยู่ระหว่าง 0% – 50%" });
        if (req.PlatformFeePercent < 0 || req.PlatformFeePercent > 30)
            return BadRequest(new { message = "ค่าธรรมเนียมระบบต้องอยู่ระหว่าง 0% – 30%" });
        if (req.GlobalMinBidIncrement < 1)
            return BadRequest(new { message = "ขั้นต่ำการเสนอราคาต้องมากกว่าหรือเท่ากับ 1 บาท" });

        var settings = await lifecycle.GetSettingsAsync();
        settings.SameCategoryDiscountPercent = req.SameCategoryDiscountPercent;
        settings.PlatformFeePercent = req.PlatformFeePercent;
        settings.GlobalMinBidIncrement = req.GlobalMinBidIncrement;
        if (!string.IsNullOrWhiteSpace(req.PlatformPromptPayId))
            settings.PlatformPromptPayId = req.PlatformPromptPayId.Trim();
        if (!string.IsNullOrWhiteSpace(req.PlatformAccountName))
            settings.PlatformAccountName = req.PlatformAccountName.Trim();
        settings.UpdatedAt = DateTime.UtcNow;

        await Db.SaveChangesAsync();
        await lifecycle.RecalculateUnpaidOrdersAsync();

        return Ok(new { message = "บันทึกกติกาการเงินและข้อมูล QR กลางเรียบร้อยแล้ว", settings });
    }

    [HttpGet("report/csv")]
    public async Task<IActionResult> ExportCsvReport()
    {
        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        static string Csv(string? v) => $"\"{(v ?? "").Replace("\"", "\"\"")}\"";

        var sb = new StringBuilder();
        sb.AppendLine("รหัสรายการ,สินค้า,หมวดหมู่,ผู้ชนะประมูล,ผู้ขาย,วิธีชำระเงิน,ราคาปิดประมูล,ส่วนลด(%),ส่วนลด(บาท),ยอดชำระสุทธิ,ค่าธรรมเนียม(%),ค่าธรรมเนียม(บาท),ยอดโอนผู้ขายสุทธิ,สถานะชำระเงิน,สถานะนัดรับ,สถานะปล่อยเงิน,วันที่สร้าง,หมายเหตุการเงิน");

        foreach (var o in orders)
        {
            var methodLabel = o.PaymentMethod == PaymentMethods.DirectToSeller ? "จ่ายตรงผู้ขาย" : "QR ระบบกลาง (Escrow)";
            sb.AppendLine(string.Join(",",
                Csv(o.OrderCode),
                Csv(o.AuctionItem?.Name),
                Csv(o.AuctionItem?.Category?.Name),
                Csv(o.Buyer?.DisplayName),
                Csv(o.Seller?.ShopName ?? o.Seller?.DisplayName),
                Csv(methodLabel),
                o.WinningPrice.ToString("0.00"),
                o.DiscountPercent.ToString("0.##"),
                o.DiscountAmount.ToString("0.00"),
                o.FinalBuyerAmount.ToString("0.00"),
                o.PlatformFeePercent.ToString("0.##"),
                o.PlatformFeeAmount.ToString("0.00"),
                o.SellerNetAmount.ToString("0.00"),
                Csv(o.PaymentStatus),
                Csv(o.MeetupStatus),
                Csv(o.PayoutStatus),
                Csv(o.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
                Csv(o.FinanceNote)));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"sales-report-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }
}
