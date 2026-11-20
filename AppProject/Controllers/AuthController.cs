using System.Security.Claims;
using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Route("api/auth")]
public class AuthController(AppDbContext db, AuctionLifecycleService lifecycle) : ApiBaseController(db)
{
    public record LoginRequest(string Email, string Password);

    public record RegisterRequest(
        string DisplayName,
        string Email,
        string Password,
        string Role,
        string? ShopName,
        string? Phone,
        string? BankAccount,
        string? SellerBio);

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        return Ok(new
        {
            authenticated = user is not null,
            user = user is null ? null : MapUserSummary(user),
            settings = new
            {
                settings.SameCategoryDiscountPercent,
                settings.PlatformFeePercent,
                settings.GlobalMinBidIncrement
            }
        });
    }

    [HttpGet("demo-accounts")]
    public IActionResult DemoAccounts()
    {
        return Ok(new[]
        {
            new { role = Roles.Buyer, label = "ผู้ประมูล (Buyer)", email = "buyer@oldthings.th", name = "คุณพิมพ์ชนก", note = "มีส่วนลดซื้อหมวดเดียวกัน 10% พร้อมทดสอบชำระเงิน" },
            new { role = Roles.Seller, label = "ผู้ขาย (Seller)", email = "seller@oldthings.th", name = "คุณชาญชัย", note = "อนุมัติแล้ว ลงสินค้า นัดรับ ดูยอดขายได้ทันที" },
            new { role = Roles.Admin, label = "แอดมิน (Admin)", email = "admin@oldthings.th", name = "แอดมินสุวิจักขณ์", note = "อนุมัติผู้ขาย/สินค้า ระงับบัญชี หมวดหมู่ ข้อพิพาท" },
            new { role = Roles.Finance, label = "การเงิน (Finance)", email = "finance@oldthings.th", name = "ฝ่ายการเงินนิภา", note = "ปล่อยเงิน คืนเงิน ตั้งกติกา ออกรายงาน CSV" }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "กรุณากรอกอีเมลและรหัสผ่าน" });

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null || !PasswordService.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง" });

        if (user.IsSuspended)
            return StatusCode(403, new { message = $"บัญชีนี้ถูกระงับการใช้งาน{(string.IsNullOrWhiteSpace(user.SuspendReason) ? "" : $": {user.SuspendReason}")}" });

        await SignInAsync(user);
        return Ok(new { message = "เข้าสู่ระบบสำเร็จ", user = MapUserSummary(user) });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var displayName = (req.DisplayName ?? "").Trim();
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var password = req.Password ?? "";
        var role = req.Role == Roles.Seller ? Roles.Seller : Roles.Buyer;

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            return BadRequest(new { message = "กรุณาระบุชื่อที่แสดงอย่างน้อย 2 ตัวอักษร" });
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "รูปแบบอีเมลไม่ถูกต้อง" });
        if (password.Length < 6)
            return BadRequest(new { message = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร" });

        var exists = await Db.Users.AnyAsync(u => u.Email.ToLower() == email);
        if (exists)
            return Conflict(new { message = "อีเมลนี้ถูกใช้งานแล้ว" });

        if (role == Roles.Seller)
        {
            if (string.IsNullOrWhiteSpace(req.ShopName) || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.BankAccount))
                return BadRequest(new { message = "การสมัครเป็นผู้ขายต้องระบุชื่อร้าน เบอร์โทรศัพท์ และบัญชีรับเงิน" });
        }

        var user = new User
        {
            DisplayName = displayName,
            Email = email,
            PasswordHash = PasswordService.Hash(password),
            Role = role,
            SellerStatus = role == Roles.Seller ? SellerStatuses.Pending : SellerStatuses.None,
            ShopName = role == Roles.Seller ? req.ShopName?.Trim() : null,
            Phone = req.Phone?.Trim(),
            BankAccount = role == Roles.Seller ? req.BankAccount?.Trim() : null,
            SellerBio = role == Roles.Seller ? req.SellerBio?.Trim() : null,
            SellerAppliedAt = role == Roles.Seller ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };

        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        await SignInAsync(user);

        var msg = role == Roles.Seller
            ? "สมัครสมาชิกผู้ขายสำเร็จ กรุณารอแอดมินอนุมัติก่อนลงขายสินค้า"
            : "สมัครสมาชิกผู้ประมูลสำเร็จ";
        return Ok(new { message = msg, user = MapUserSummary(user) });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "ออกจากระบบแล้ว" });
    }

    private async Task SignInAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    }
}
