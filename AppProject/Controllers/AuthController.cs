using System.Security.Claims;
using System.Security.Cryptography;
using AppProject.Data;
using AppProject.Models;
using AppProject.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Controllers;

[Route("api/auth")]
public class AuthController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

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

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record ForgotPasswordRequest(string Email);
    public record ResetPasswordRequest(string Email, string ResetToken, string NewPassword);

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var settings = await lifecycle.GetSettingsAsync();
        var user = await GetCurrentActiveUserAsync();
        if (user is not null)
        {
            var cookieRole = User.FindFirstValue(ClaimTypes.Role);
            if (!string.Equals(cookieRole, user.Role, StringComparison.Ordinal))
            {
                await SignInAsync(user);
            }
        }
        return Ok(new
        {
            authenticated = user is not null,
            isDevelopment = env.IsDevelopment(),
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
        if (!env.IsDevelopment())
            return NotFound(new { message = "โหมดบัญชีทดลองเปิดเฉพาะสภาพแวดล้อมพัฒนา (Development) เท่านั้น" });

        return Ok(new[]
        {
            new { role = Roles.Buyer, label = "ผู้ประมูล (Buyer)", email = "buyer@oldthings.th", name = "คุณพิมพ์ชนก", note = "มีส่วนลดซื้อหมวดเดียวกัน 10% พร้อมทดสอบชำระเงิน" },
            new { role = Roles.Seller, label = "ผู้ขาย (Seller)", email = "seller@oldthings.th", name = "คุณชาญชัย", note = "อนุมัติแล้ว ลงสินค้า นัดรับ ประมูลซื้อของอื่นได้ด้วย" },
            new { role = Roles.Admin, label = "แอดมิน (Admin)", email = "admin@oldthings.th", name = "แอดมินสุวิจักขณ์", note = "อนุมัติผู้ขาย/สินค้า ระงับบัญชี หมวดหมู่ ข้อพิพาท Audit Log" },
            new { role = Roles.Finance, label = "การเงิน (Finance)", email = "finance@oldthings.th", name = "ฝ่ายการเงินนิภา", note = "ตรวจสลิป ปล่อยเงิน คืนเงิน ตั้งกติกา รายงาน CSV" }
        });
    }

    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "กรุณากรอกอีเมลและรหัสผ่าน" });

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null)
            return Unauthorized(new { message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง" });

        var now = DateTime.UtcNow;
        if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > now)
        {
            var remainMin = Math.Max(1, (int)Math.Ceiling((user.LockoutEndAt.Value - now).TotalMinutes));
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"ลองเข้าสู่ระบบผิดหลายครั้ง บัญชีถูกล็อกชั่วคราวอีกประมาณ {remainMin} นาที"
            });
        }

        if (!PasswordService.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLoginAttempts)
            {
                user.LockoutEndAt = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
                await Db.SaveChangesAsync();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = $"รหัสผ่านผิดครบ {MaxFailedLoginAttempts} ครั้ง บัญชีถูกล็อกชั่วคราว {(int)LockoutDuration.TotalMinutes} นาที"
                });
            }

            var remainingTries = MaxFailedLoginAttempts - user.FailedLoginCount;
            await Db.SaveChangesAsync();
            return Unauthorized(new { message = $"อีเมลหรือรหัสผ่านไม่ถูกต้อง (เหลือโอกาสลองอีก {remainingTries} ครั้ง)" });
        }

        if (user.IsSuspended)
            return StatusCode(403, new { message = $"บัญชีนี้ถูกระงับการใช้งาน{(string.IsNullOrWhiteSpace(user.SuspendReason) ? "" : $": {user.SuspendReason}")}" });

        if (user.FailedLoginCount > 0 || user.LockoutEndAt.HasValue)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndAt = null;
            await Db.SaveChangesAsync();
        }

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

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "กรุณากรอกรหัสผ่านเดิมและรหัสผ่านใหม่" });
        if (req.NewPassword.Length < 6)
            return BadRequest(new { message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร" });
        if (!PasswordService.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "รหัสผ่านเดิมไม่ถูกต้อง" });

        user.PasswordHash = PasswordService.Hash(req.NewPassword);
        await Db.SaveChangesAsync();
        return Ok(new { message = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว" });
    }

    [EnableRateLimiting("login")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "กรุณากรอกอีเมลให้ถูกต้อง" });

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        string? devCode = null;
        if (user is not null && !user.IsSuspended)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetToken = code;
            user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await Db.SaveChangesAsync();
            if (env.IsDevelopment()) devCode = code;
        }

        return Ok(new
        {
            message = "หากมีอีเมลนี้ในระบบ รหัสรีเซ็ต 6 หลักจะถูกสร้างและใช้งานได้ภายใน 15 นาที",
            devResetCode = devCode
        });
    }

    [EnableRateLimiting("login")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var token = (req.ResetToken ?? "").Trim();
        var newPassword = req.NewPassword ?? "";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "กรุณากรอกอีเมลและรหัสรีเซ็ต 6 หลัก" });
        if (newPassword.Length < 6)
            return BadRequest(new { message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร" });

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null
            || string.IsNullOrWhiteSpace(user.PasswordResetToken)
            || !string.Equals(user.PasswordResetToken, token, StringComparison.Ordinal)
            || !user.PasswordResetExpiresAt.HasValue
            || user.PasswordResetExpiresAt.Value < DateTime.UtcNow)
        {
            return BadRequest(new { message = "รหัสรีเซ็ตไม่ถูกต้องหรือหมดอายุแล้ว กรุณาขอรหัสใหม่" });
        }

        user.PasswordHash = PasswordService.Hash(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ตั้งรหัสผ่านใหม่เรียบร้อยแล้ว สามารถเข้าสู่ระบบได้ทันที" });
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
