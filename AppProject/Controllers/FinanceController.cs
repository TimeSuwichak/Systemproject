// =================================================================================================
// ไฟล์: AppProject/Controllers/FinanceController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend - REST API Controller) สำหรับระบบ "ฝ่ายการเงิน (Finance)"
// หน้าที่โดยรวม:
//   - ดึงข้อมูลแดชบอร์ดการเงิน (สรุปยอดขาย, ส่วนลด, ค่าธรรมเนียมระบบ, เงินพักรอปล่อย Escrow, เงินคืน,
//     สถานะข้อพิพาท และสิทธิ์การกดตรวจสลิป/ปล่อยเงิน/คืนเงินของแต่ละคำสั่งซื้อ)
//   - ตรวจสอบสลิปการโอนเงินผ่าน QR ระบบกลาง (PlatformEscrow) ทั้งอนุมัติและปฏิเสธพร้อมแจ้งเตือนผู้ซื้อ/ผู้ขาย
//   - ปล่อยเงินสุทธิให้ผู้ขาย (Release Payout) ภายใต้เงื่อนไขความปลอดภัย (ต้องไม่มีข้อพิพาทเปิดอยู่
//     และผู้ซื้อยืนยันรับของแล้วหรือครบกำหนดวันปล่อยอัตโนมัติหลังส่งมอบ)
//   - คืนเงินเต็มจำนวนให้ผู้ซื้อ (Refund) สำหรับรายการที่พักเงินอยู่ในระบบกลาง
//   - ปรับปรุงกติกาการเงินและข้อมูล PromptPay QR ระบบกลาง (ส่วนลดหมวดเดียวกัน, ค่าธรรมเนียม, ขั้นต่ำการบิด)
//   - ส่งออกรายงานยอดขายทั้งหมดเป็นไฟล์ CSV (UTF-8 BOM รองรับภาษาไทยใน Excel)
//   - บันทึก Audit Log ทุกการกระทำสำคัญทางการเงินเพื่อความโปร่งใสตรวจสอบย้อนหลังได้
// การเชื่อมต่อกับไฟล์/เลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): เชื่อมต่อจาก wwwroot/js/site.js ผ่านฟังก์ชัน api() (ครอบ fetch())
//     และลิงก์ดาวน์โหลด CSV โดยตรงใน Views/Home/Index.cshtml (หน้า #view-finance-payments, #view-finance-rules, #view-finance-report)
//     โดยแนบ Cookie ยืนยันตัวตน "OldThingsAuth" (สิทธิ์ Role = Finance)
//   - ฝั่งหลังบ้าน (Backend): สืบทอดจาก ApiBaseController (ใช้ GetCurrentActiveUserAsync, AddAuditLog),
//     ติดต่อฐานข้อมูลผ่าน AppDbContext, และใช้ AuctionLifecycleService (ตรวจสิทธิ์ CanReleaseEscrow, คำนวณยอดค้างชำระใหม่)
// =================================================================================================

using System.Text;
using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

// กำหนดสิทธิ์ระดับคลาส: อนุญาตเฉพาะผู้ใช้ที่มี Role = "Finance" เท่านั้น
[Authorize(Roles = Roles.Finance)]
// กำหนดเส้นทางหลัก (Base Route) ของ Controller นี้เป็น /api/finance
[Route("api/finance")]
public class FinanceController(AppDbContext db, AuctionLifecycleService lifecycle) : ApiBaseController(db)
{
    // DTO สำหรับรับหมายเหตุจากเจ้าหน้าที่การเงินตอนกดปล่อยเงิน (Release) หรือคืนเงิน (Refund)
    public record FinanceActionRequest(string? Note);

    // DTO สำหรับรับผลการตรวจสอบสลิปเงินพักระบบกลาง (อนุมัติหรือปฏิเสธพร้อมเหตุผล)
    public record VerifyEscrowPaymentRequest(bool Approve, string? Reason);

    // DTO สำหรับรับค่าปรับปรุงกติกาการเงินและบัญชี PromptPay ระบบกลาง
    public record UpdateSettingsRequest(
        decimal SameCategoryDiscountPercent, // เปอร์เซ็นต์ส่วนลดเมื่อชนะประมูลสินค้าหมวดเดียวกันเพิ่ม
        decimal PlatformFeePercent,          // เปอร์เซ็นต์ค่าธรรมเนียมระบบสำหรับรายการ Escrow
        decimal GlobalMinBidIncrement,       // ขั้นต่ำการเสนอราคาเพิ่มระดับทั้งระบบ (บาท)
        string? PlatformPromptPayId,         // หมายเลขพร้อมเพย์ระบบกลางสำหรับสร้าง QR
        string? PlatformAccountName);        // ชื่อบัญชีรับเงินระบบกลาง

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : GET /api/finance/dashboard
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ฟังก์ชัน loadFinanceDashboard()
    //                        แสดงผลใน Views/Home/Index.cshtml หน้า #view-finance-payments, #view-finance-rules, #view-finance-report
    // ข้อมูลที่รับจากหน้าบ้าน : ไม่มีพารามิเตอร์ (ยืนยันตัวตนผ่าน Cookie "OldThingsAuth", สิทธิ์ Role = Finance)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON ประกอบด้วยกติกาปัจจุบัน (settings), ยอดสรุปทางการเงิน (summary),
    //                        และรายการคำสั่งซื้อทั้งหมดพร้อมสถานะข้อพิพาท/เหตุผลที่ปล่อยเงินไม่ได้/ธงปุ่มกด
    //                        (canVerifyEscrowPayment, canRelease, canRefund)
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var now = DateTime.UtcNow;
        // ดึงการตั้งค่าการเงินปัจจุบันของระบบ
        var settings = await lifecycle.GetSettingsAsync();
        // ดึงรายการคำสั่งซื้อทั้งหมด พร้อมข้อมูลสินค้า หมวดหมู่ ผู้ซื้อ และผู้ขาย เรียงจากใหม่ไปเก่า
        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        // สรุปสถานะข้อพิพาท (Disputes) ที่ผูกกับแต่ละ OrderPaymentId เพื่อใช้ระงับการปล่อยเงินอัตโนมัติหากมีเคสเปิดอยู่
        var disputesByOrder = await Db.Disputes
            .Where(d => d.OrderPaymentId != null)
            .GroupBy(d => d.OrderPaymentId!.Value)
            .Select(g => new { OrderId = g.Key, HasOpen = g.Any(x => x.Status != DisputeStatuses.Resolved), LatestStatus = g.OrderByDescending(x => x.CreatedAt).First().Status })
            .ToDictionaryAsync(x => x.OrderId);

        // คัดแยกกลุ่มออเดอร์ที่ชำระเงินแล้วทั้งหมด และกลุ่มที่ชำระผ่านระบบกลาง (Escrow) เพื่อนำไปคำนวณสรุปยอด
        var paidOrders = orders.Where(o => o.PaymentStatus == PaymentStatuses.Paid).ToList();
        var escrowPaidOrders = paidOrders.Where(o => o.PaymentMethod == PaymentMethods.PlatformEscrow).ToList();

        return Ok(new
        {
            // กติกาการเงินและข้อมูลพร้อมเพย์ปัจจุบันสำหรับเติมลงฟอร์ม #financeRulesForm
            settings = new
            {
                settings.SameCategoryDiscountPercent,
                settings.PlatformFeePercent,
                settings.GlobalMinBidIncrement,
                settings.PlatformPromptPayId,
                settings.PlatformAccountName,
                settings.UpdatedAt
            },
            // ตัวเลขสรุป KPI ทางการเงินสำหรับการ์ดสถิติและหน้ารายงานยอดขาย
            summary = new
            {
                totalOrders = orders.Count,
                paidOrdersCount = paidOrders.Count,
                unpaidOrdersCount = orders.Count(o => o.PaymentStatus == PaymentStatuses.Unpaid),
                // จำนวนสลิป Escrow ที่รอฝ่ายการเงินตรวจสอบ
                pendingVerifyCount = orders.Count(o => o.PaymentStatus == PaymentStatuses.PendingVerification && o.PaymentMethod == PaymentMethods.PlatformEscrow),
                // ยอดราคาปิดประมูลรวม (ก่อนหักส่วนลด) ของรายการที่ชำระแล้ว
                grossSales = paidOrders.Sum(o => o.WinningPrice),
                // มูลค่าส่วนลดหมวดเดียวกันรวมทั้งหมด
                totalDiscounts = paidOrders.Sum(o => o.DiscountAmount),
                // ยอดเงินที่ผู้ซื้อชำระจริงรวมทั้งหมด
                totalBuyerPaid = paidOrders.Sum(o => o.FinalBuyerAmount),
                // รายได้ค่าธรรมเนียมแพลตฟอร์มรวม (เฉพาะรายการ Escrow)
                totalPlatformFee = escrowPaidOrders.Sum(o => o.PlatformFeeAmount),
                // ยอดเงินสุทธิที่ฝ่ายการเงินโอนปล่อยให้ผู้ขายไปแล้ว
                totalReleased = orders.Where(o => o.PayoutStatus == PayoutStatuses.Released).Sum(o => o.SellerNetAmount),
                // ยอดเงินสุทธิที่ยังพักอยู่ในระบบกลาง (Escrow) รอปล่อยให้ผู้ขาย
                pendingRelease = escrowPaidOrders.Where(o => o.PayoutStatus == PayoutStatuses.Pending).Sum(o => o.SellerNetAmount),
                // ยอดเงินที่คืนให้ผู้ซื้อไปแล้วทั้งหมด
                totalRefunded = orders.Where(o => o.PaymentStatus == PaymentStatuses.Refunded).Sum(o => o.FinalBuyerAmount)
            },
            // รายละเอียดแต่ละคำสั่งซื้อพร้อมคำนวณเงื่อนไขปุ่มดำเนินการฝั่งการเงิน
            orders = orders.Select(o =>
            {
                disputesByOrder.TryGetValue(o.Id, out var disp);
                var isEscrow = o.PaymentMethod == PaymentMethods.PlatformEscrow;
                var hasOpenDispute = disp?.HasOpen ?? false;
                // ตรวจสอบเงื่อนไขว่าออเดอร์นี้พร้อมปล่อยเงินให้ผู้ขายแล้วหรือยัง (ใช้กฎกลางจาก AuctionLifecycleService)
                var canRelease = AuctionLifecycleService.CanReleaseEscrow(o, hasOpenDispute, now);

                // สร้างข้อความอธิบายเหตุผลที่ยังกดปล่อยเงินไม่ได้ เพื่อแสดงเป็น Tooltip/Badge ให้ฝ่ายการเงินเข้าใจทันที
                string? releaseBlockedReason = null;
                if (isEscrow && o.PaymentStatus == PaymentStatuses.Paid && o.PayoutStatus == PayoutStatuses.Pending && !canRelease)
                {
                    if (hasOpenDispute)
                        releaseBlockedReason = "ระงับการปล่อยเงินชั่วคราว (มีข้อพิพาทเปิดอยู่)";
                    else if (o.MeetupStatus == MeetupStatuses.Delivered)
                        releaseBlockedReason = $"รอผู้ซื้อยืนยันรับของ (หรือครบ {AuctionLifecycleService.AutoReleaseAfterDeliveredDays} วันหลังส่งมอบ)";
                    else
                        releaseBlockedReason = "รอผู้ขายส่งมอบและผู้ซื้อยืนยันรับของ";
                }

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
                    o.PaymentDueAt,
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
                    hasOpenDispute,
                    disputeStatus = disp?.LatestStatus,
                    releaseBlockedReason,
                    // ปุ่มตรวจสลิปจะแสดงเฉพาะออเดอร์ Escrow ที่ผู้ซื้อส่งสลิปมาแล้ว (PendingVerification)
                    canVerifyEscrowPayment = isEscrow && o.PaymentStatus == PaymentStatuses.PendingVerification,
                    canRelease,
                    // ปุ่มคืนเงินจะแสดงเฉพาะออเดอร์ Escrow ที่ชำระเงินแล้วและยังไม่ถูกปล่อยเงินให้ผู้ขาย
                    canRefund = isEscrow
                        && o.PaymentStatus == PaymentStatuses.Paid
                        && o.PayoutStatus == PayoutStatuses.Pending,
                    o.CreatedAt
                };
            })
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/finance/orders/{orderId}/verify-payment
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-finance-verify-pay] (ส่ง approve: true)
    //                        และปุ่ม [data-finance-reject-pay] (เปิด Modal กรอกเหตุผล ส่ง approve: false, reason) ในหน้า #view-finance-payments
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} + JSON Body (VerifyEscrowPaymentRequest: approve, reason)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Finance)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message }
    //                        - กรณี Approve: ปรับเป็น Paid, แจ้งเตือนทั้งผู้ซื้อและผู้ขาย, บันทึก AuditLog, คำนวณส่วนลดออเดอร์ค้างของผู้ซื้อใหม่
    //                        - กรณี Reject: ดีดกลับเป็น Unpaid พร้อมเหตุผล, แจ้งเตือนผู้ซื้อ, บันทึก AuditLog
    //                        หรือ 400 BadRequest, 401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/verify-payment")]
    public async Task<IActionResult> VerifyEscrowPayment(int orderId, [FromBody] VerifyEscrowPaymentRequest req)
    {
        var actor = await GetCurrentActiveUserAsync();
        if (actor is null) return Unauthorized();

        // ดึงข้อมูลคำสั่งซื้อพร้อมชื่อสินค้าเพื่อใช้แจ้งเตือน
        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        // ฝ่ายการเงินตรวจสอบได้เฉพาะรายการที่ชำระผ่าน QR ระบบกลาง (PlatformEscrow) เท่านั้น
        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้เป็นการชำระตรงกับผู้ขาย ต้องให้ผู้ขายเป็นผู้ยืนยัน" });
        // ต้องอยู่ในสถานะรอตรวจสอบสลิปเท่านั้น
        if (order.PaymentStatus != PaymentStatuses.PendingVerification)
            return BadRequest(new { message = "รายการนี้ไม่ได้อยู่ในสถานะรอตรวจสลิป" });

        var now = DateTime.UtcNow;
        if (req.Approve)
        {
            // ยืนยันสลิปผ่าน: ปรับสถานะการชำระเป็น Paid และล้างเหตุผลปฏิเสธเดิม (ถ้ามี)
            order.PaymentStatus = PaymentStatuses.Paid;
            order.PaidAt = now;
            order.PaymentRejectReason = null;

            // ส่งการแจ้งเตือนให้ผู้ซื้อว่าสลิปผ่านการตรวจสอบแล้ว
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
            // ส่งการแจ้งเตือนให้ผู้ขายทราบว่าผู้ซื้อจ่ายเงินเข้าระบบกลางแล้ว สามารถกำหนดนัดรับสินค้าได้เลย
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

            // บันทึกประวัติการตรวจสอบสลิปลง Audit Log
            AddAuditLog(actor, "VerifyEscrowSlip", "OrderPayment", order.Id, $"ยืนยันสลิป {order.OrderCode} ยอด ฿{order.FinalBuyerAmount:N2}");
            await Db.SaveChangesAsync();
            // คำนวณส่วนลดหมวดเดียวกันให้ออเดอร์อื่นที่ผู้ซื้อรายนี้ยังค้างชำระอยู่
            await lifecycle.RecalculateUnpaidOrdersAsync(order.BuyerId);
            return Ok(new { message = $"ยืนยันรับชำระเงิน {order.OrderCode} แล้ว" });
        }
        else
        {
            // ปฏิเสธสลิป: บังคับกรอกเหตุผลเพื่อให้ผู้ซื้อทราบปัญหาและอัปโหลดใหม่ได้
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest(new { message = "กรุณาระบุเหตุผลในการปฏิเสธสลิป" });

            order.PaymentStatus = PaymentStatuses.Unpaid;
            order.PaymentRejectReason = req.Reason.Trim();

            // แจ้งเตือนผู้ซื้อให้ส่งสลิปใหม่
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

            // บันทึก Audit Log การปฏิเสธสลิป
            AddAuditLog(actor, "RejectEscrowSlip", "OrderPayment", order.Id, $"ปฏิเสธสลิป {order.OrderCode}: {order.PaymentRejectReason}");
            await Db.SaveChangesAsync();
            return Ok(new { message = $"ปฏิเสธสลิป {order.OrderCode} พร้อมส่งเหตุผลให้ผู้ซื้อแล้ว" });
        }
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/finance/orders/{orderId}/release
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-finance-release] (เปิด Modal กรอกหมายเหตุการโอน)
    //                        ในหน้า #view-finance-payments
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} + JSON Body (FinanceActionRequest?: note)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Finance)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (ปรับ PayoutStatus เป็น Released, แจ้งเตือนผู้ขาย, บันทึก AuditLog)
    //                        หรือ 400 BadRequest (ไม่ใช่ Escrow / ยังไม่จ่าย / ดำเนินการแล้ว / มีข้อพิพาทเปิดอยู่ / ยังไม่ครบเงื่อนไขปล่อยเงิน),
    //                        401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/release")]
    public async Task<IActionResult> ReleasePayout(int orderId, [FromBody] FinanceActionRequest? req)
    {
        var actor = await GetCurrentActiveUserAsync();
        if (actor is null) return Unauthorized();

        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        // ตรวจสอบเงื่อนไขพื้นฐานก่อนปล่อยเงิน Escrow
        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้ผู้ขายรับเงินตรงจากผู้ซื้อแล้ว ไม่ต้องปล่อยเงินผ่านระบบกลาง" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "ไม่สามารถปล่อยเงินได้ เนื่องจากรายการนี้ยังไม่ยืนยันการชำระเงินหรือถูกคืนเงินแล้ว" });
        if (order.PayoutStatus != PayoutStatuses.Pending)
            return BadRequest(new { message = "รายการนี้ดำเนินการด้านการเงินไปแล้ว" });

        // กฎความปลอดภัย Escrow: ห้ามปล่อยเงินหากยังมีข้อพิพาท (Dispute) ที่ยังไม่ได้รับการแก้ไข (Status != Resolved)
        var hasOpenDispute = await Db.Disputes.AnyAsync(d => d.OrderPaymentId == order.Id && d.Status != DisputeStatuses.Resolved);
        if (hasOpenDispute)
            return BadRequest(new { message = "ไม่สามารถปล่อยเงินได้ เนื่องจากรายการนี้มีข้อพิพาทที่ยังไม่ได้รับการแก้ไข" });

        // กฎเงื่อนไขการส่งมอบ: ผู้ซื้อต้องยืนยันรับสินค้า (Completed) หรือครบกำหนดวันปล่อยอัตโนมัติหลังผู้ขายส่งมอบ
        var now = DateTime.UtcNow;
        if (!AuctionLifecycleService.CanReleaseEscrow(order, false, now))
            return BadRequest(new { message = $"ต้องรอให้ผู้ซื้อยืนยันรับสินค้าแล้ว (Completed) หรือครบ {AuctionLifecycleService.AutoReleaseAfterDeliveredDays} วันหลังผู้ขายส่งมอบก่อนจึงจะปล่อยเงินได้" });

        // บันทึกการปล่อยเงินสุทธิให้ผู้ขาย (SellerNetAmount คือยอดหลังหักค่าธรรมเนียมระบบแล้ว)
        order.PayoutStatus = PayoutStatuses.Released;
        order.PayoutReleasedAt = now;
        order.FinanceNote = string.IsNullOrWhiteSpace(req?.Note)
            ? "โอนเงินสุทธิให้ผู้ขายเรียบร้อยแล้ว"
            : req!.Note!.Trim();

        // แจ้งเตือนผู้ขายว่าฝ่ายการเงินโอนเงินสุทธิให้แล้ว
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

        // บันทึก Audit Log การปล่อยเงิน
        AddAuditLog(actor, "ReleasePayout", "OrderPayment", order.Id, $"ปล่อยเงิน {order.OrderCode} ยอดสุทธิ ฿{order.SellerNetAmount:N2} ({order.FinanceNote})");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"ปล่อยเงิน ฿{order.SellerNetAmount:N2} ให้ผู้ขาย ({order.OrderCode}) เรียบร้อยแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : POST /api/finance/orders/{orderId}/refund
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> ปุ่ม [data-finance-refund] (เปิด Modal กรอกหมายเหตุการคืนเงิน)
    //                        ในหน้า #view-finance-payments
    // ข้อมูลที่รับจากหน้าบ้าน : Route param {orderId:int} + JSON Body (FinanceActionRequest?: note)
    //                        + Cookie "OldThingsAuth" (สิทธิ์ Role = Finance)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message } (ปรับทั้ง PaymentStatus และ PayoutStatus เป็น Refunded,
    //                        แจ้งเตือนผู้ซื้อ, บันทึก AuditLog)
    //                        หรือ 400 BadRequest (ไม่ใช่ Escrow / ยังไม่ชำระ / ปล่อยเงินให้ผู้ขายไปแล้ว), 401 Unauthorized, 404 NotFound
    [HttpPost("orders/{orderId:int}/refund")]
    public async Task<IActionResult> RefundOrder(int orderId, [FromBody] FinanceActionRequest? req)
    {
        var actor = await GetCurrentActiveUserAsync();
        if (actor is null) return Unauthorized();

        var order = await Db.OrderPayments
            .Include(o => o.AuctionItem)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound(new { message = "ไม่พบรายการชำระเงิน" });

        // ตรวจสอบว่ามีเงินพักอยู่ในระบบกลางจริงและยังไม่ถูกโอนปล่อยให้ผู้ขาย
        if (order.PaymentMethod != PaymentMethods.PlatformEscrow)
            return BadRequest(new { message = "รายการนี้ชำระตรงกับผู้ขาย ไม่มีเงินพักอยู่ในระบบกลาง" });
        if (order.PaymentStatus != PaymentStatuses.Paid)
            return BadRequest(new { message = "รายการนี้ยังไม่ได้ชำระเงินหรือคืนเงินไปแล้ว" });
        if (order.PayoutStatus == PayoutStatuses.Released)
            return BadRequest(new { message = "ไม่สามารถคืนเงินได้ เนื่องจากปล่อยเงินให้ผู้ขายไปแล้ว" });

        // ปรับสถานะการชำระและสถานะการปล่อยเงินเป็น "คืนเงินแล้ว (Refunded)" พร้อมบันทึกเวลาและหมายเหตุ
        var now = DateTime.UtcNow;
        order.PaymentStatus = PaymentStatuses.Refunded;
        order.PayoutStatus = PayoutStatuses.Refunded;
        order.RefundedAt = now;
        order.FinanceNote = string.IsNullOrWhiteSpace(req?.Note)
            ? "คืนเงินเต็มจำนวนให้ผู้ประมูลเรียบร้อยแล้ว"
            : req!.Note!.Trim();

        // ส่งการแจ้งเตือนให้ผู้ซื้อทราบว่าฝ่ายการเงินคืนเงินเต็มจำนวนแล้ว
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

        // บันทึก Audit Log การคืนเงิน
        AddAuditLog(actor, "RefundOrder", "OrderPayment", order.Id, $"คืนเงิน {order.OrderCode} ยอด ฿{order.FinalBuyerAmount:N2} ({order.FinanceNote})");
        await Db.SaveChangesAsync();
        return Ok(new { message = $"คืนเงิน ฿{order.FinalBuyerAmount:N2} ให้ผู้ซื้อ ({order.OrderCode}) เรียบร้อยแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : PUT /api/finance/settings
    // หน้าบ้านที่เรียกใช้งาน  : wwwroot/js/site.js -> event "submit" ของฟอร์ม #financeRulesForm (หน้า #view-finance-rules)
    // ข้อมูลที่รับจากหน้าบ้าน : JSON Body (UpdateSettingsRequest: sameCategoryDiscountPercent, platformFeePercent,
    //                        globalMinBidIncrement, platformPromptPayId, platformAccountName) + Cookie "OldThingsAuth" (Role = Finance)
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK JSON { message, settings } (บันทึกกติกาใหม่, ลง AuditLog, และคำนวณยอดออเดอร์ค้างชำระใหม่ทั้งระบบ)
    //                        หรือ 400 BadRequest (ค่าอยู่นอกช่วงที่อนุญาต), 401 Unauthorized
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        var actor = await GetCurrentActiveUserAsync();
        if (actor is null) return Unauthorized();

        // ตรวจสอบขอบเขตค่าตัวเลขเพื่อป้องกันการตั้งค่าผิดพลาดร้ายแรง
        if (req.SameCategoryDiscountPercent < 0 || req.SameCategoryDiscountPercent > 50)
            return BadRequest(new { message = "ส่วนลดหมวดเดียวกันต้องอยู่ระหว่าง 0% – 50%" });
        if (req.PlatformFeePercent < 0 || req.PlatformFeePercent > 30)
            return BadRequest(new { message = "ค่าธรรมเนียมระบบต้องอยู่ระหว่าง 0% – 30%" });
        if (req.GlobalMinBidIncrement < 1)
            return BadRequest(new { message = "ขั้นต่ำการเสนอราคาต้องมากกว่าหรือเท่ากับ 1 บาท" });

        // อัปเดตเรคคอร์ดการตั้งค่าการเงิน (Singleton Id = 1)
        var settings = await lifecycle.GetSettingsAsync();
        settings.SameCategoryDiscountPercent = req.SameCategoryDiscountPercent;
        settings.PlatformFeePercent = req.PlatformFeePercent;
        settings.GlobalMinBidIncrement = req.GlobalMinBidIncrement;
        // อัปเดตหมายเลขพร้อมเพย์และชื่อบัญชีระบบกลางเฉพาะเมื่อส่งค่าที่ไม่ว่างมา
        if (!string.IsNullOrWhiteSpace(req.PlatformPromptPayId))
            settings.PlatformPromptPayId = req.PlatformPromptPayId.Trim();
        if (!string.IsNullOrWhiteSpace(req.PlatformAccountName))
            settings.PlatformAccountName = req.PlatformAccountName.Trim();
        settings.UpdatedAt = DateTime.UtcNow;

        // บันทึก Audit Log การเปลี่ยนแปลงกติกาการเงิน
        AddAuditLog(actor, "UpdateFinanceSettings", "FinanceSettings", 1,
            $"ส่วนลดหมวด={settings.SameCategoryDiscountPercent}%, ค่าธรรมเนียม={settings.PlatformFeePercent}%, ขั้นต่ำ=฿{settings.GlobalMinBidIncrement:N0}");
        await Db.SaveChangesAsync();
        // คำนวณส่วนลด/ค่าธรรมเนียมใหม่ให้ออเดอร์ทั้งหมดที่ยังไม่ได้ชำระเงิน (Unpaid) เพื่อให้สอดคล้องกับกติกาล่าสุดทันที
        await lifecycle.RecalculateUnpaidOrdersAsync();

        return Ok(new { message = "บันทึกกติกาการเงินและข้อมูล QR กลางเรียบร้อยแล้ว", settings });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // HTTP Method & Route : GET /api/finance/report/csv
    // หน้าบ้านที่เรียกใช้งาน  : Views/Home/Index.cshtml -> ลิงก์ดาวน์โหลด <a href="/api/finance/report/csv" download>
    //                        ในหน้า #view-finance-report (เบราว์เซอร์ส่ง GET พร้อม Cookie "OldThingsAuth" โดยตรง)
    // ข้อมูลที่รับจากหน้าบ้าน : ไม่มีพารามิเตอร์ (สิทธิ์ Role = Finance ผ่าน Cookie "OldThingsAuth")
    // ข้อมูลที่ส่งกลับหน้าบ้าน : 200 OK File Content-Type "text/csv; charset=utf-8" (แนบ UTF-8 BOM)
    //                        ชื่อไฟล์ "sales-report-yyyyMMdd-HHmm.csv" สำหรับเปิดใน Microsoft Excel โดยภาษาไทยไม่เพี้ยน
    [HttpGet("report/csv")]
    public async Task<IActionResult> ExportCsvReport()
    {
        // ดึงรายการคำสั่งซื้อทั้งหมดพร้อมความสัมพันธ์ที่จำเป็นสำหรับสร้างตารางรายงาน
        var orders = await Db.OrderPayments
            .Include(o => o.AuctionItem)!.ThenInclude(a => a!.Category)
            .Include(o => o.Buyer)
            .Include(o => o.Seller)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        // ฟังก์ชันท้องถิ่นสำหรับครอบฟิลด์ข้อความด้วยเครื่องหมายคำพูด (") และ Escape เครื่องหมาย " ซ้อนตามมาตรฐาน RFC 4180
        static string Csv(string? v) => $"\"{(v ?? "").Replace("\"", "\"\"")}\"";

        var sb = new StringBuilder();
        // เขียนแถวหัวตาราง (Header Row) ภาษาไทย
        sb.AppendLine("รหัสรายการ,สินค้า,หมวดหมู่,ผู้ชนะประมูล,ผู้ขาย,วิธีชำระเงิน,ราคาปิดประมูล,ส่วนลด(%),ส่วนลด(บาท),ยอดชำระสุทธิ,ค่าธรรมเนียม(%),ค่าธรรมเนียม(บาท),ยอดโอนผู้ขายสุทธิ,สถานะชำระเงิน,สถานะนัดรับ,สถานะปล่อยเงิน,วันที่สร้าง,หมายเหตุการเงิน");

        // วนลูปเขียนข้อมูลแต่ละคำสั่งซื้อลงทีละแถว
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

        // เติม UTF-8 BOM (Byte Order Mark) ด้านหน้าไฟล์เพื่อให้ Excel รู้ว่าเป็น UTF-8 และแสดงภาษาไทยได้ถูกต้อง
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"sales-report-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }
}
