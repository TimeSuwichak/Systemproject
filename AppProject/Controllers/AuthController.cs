// ============================================================================
// ไฟล์: Controllers/AuthController.cs
// บทบาท: ฝั่งหลังบ้าน (Backend REST API Controller) - จัดการระบบยืนยันตัวตน (Authentication)
// หน้าที่หลัก:
//   - ตรวจสอบสถานะการเข้าสู่ระบบปัจจุบัน (Session / Auth Cookie) และส่งข้อมูลตั้งค่าระบบ
//   - เข้าสู่ระบบ (Login) พร้อมป้องกัน Brute-Force (Rate Limiting + ล็อกบัญชีชั่วคราว)
//   - สมัครสมาชิก (Register) ทั้งผู้ประมูล (Buyer) และผู้ขาย (Seller)
//   - เปลี่ยนรหัสผ่าน (Change Password), ลืมรหัสผ่าน (Forgot Password), รีเซ็ตรหัสผ่าน (Reset Password)
//   - ออกจากระบบ (Logout) และส่งรายการบัญชีทดลอง (Demo Accounts) ในโหมดพัฒนา (Development)
// การเชื่อมต่อกับเลเยอร์อื่น:
//   - ฝั่งหน้าบ้าน (Frontend): `wwwroot/js/site.js` เรียกผ่านฟังก์ชัน `api()` (ใช้ `fetch()` + `credentials: "same-origin"`)
//     โดยเบราว์เซอร์จะแนบ/รับคุกกี้ยืนยันตัวตนชื่อ `OldThingsAuth` (Cookie Authentication) อัตโนมัติ
//   - เลเยอร์ฐานข้อมูล/บริการ: สืบทอดจาก `ApiBaseController`, ใช้ `AppDbContext` (ฐานข้อมูล),
//     `AuctionLifecycleService` (ดึงกติการะบบ), และ `PasswordService` (แฮช/ตรวจสอบรหัสผ่าน)
// ============================================================================

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

// กำหนด Route หลักของ Controller นี้เป็น /api/auth
[Route("api/auth")]
public class AuthController(AppDbContext db, AuctionLifecycleService lifecycle, IWebHostEnvironment env) : ApiBaseController(db)
{
    // จำนวนครั้งสูงสุดที่อนุญาตให้กรอกรหัสผ่านผิดก่อนล็อกบัญชีชั่วคราว (5 ครั้ง)
    private const int MaxFailedLoginAttempts = 5;
    // ระยะเวลาล็อกบัญชีชั่วคราวเมื่อกรอกรหัสผ่านผิดครบกำหนด (5 นาที)
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    // DTO สำหรับรับข้อมูลเข้าสู่ระบบจากหน้าบ้าน (อีเมล และ รหัสผ่าน)
    public record LoginRequest(string Email, string Password);

    // DTO สำหรับรับข้อมูลสมัครสมาชิกจากหน้าบ้าน (รองรับทั้ง Buyer และข้อมูลร้านค้าเพิ่มเติมสำหรับ Seller)
    public record RegisterRequest(
        string DisplayName,
        string Email,
        string Password,
        string Role,
        string? ShopName,
        string? Phone,
        string? BankAccount,
        string? SellerBio);

    // DTO สำหรับรับข้อมูลเปลี่ยนรหัสผ่าน (รหัสผ่านเดิม และ รหัสผ่านใหม่)
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    // DTO สำหรับขอรหัสรีเซ็ตรหัสผ่าน 6 หลัก (ระบุอีเมล)
    public record ForgotPasswordRequest(string Email);
    // DTO สำหรับตั้งรหัสผ่านใหม่ด้วยรหัสรีเซ็ต 6 หลัก (อีเมล, รหัสรีเซ็ต, รหัสผ่านใหม่)
    public record ResetPasswordRequest(string Email, string ResetToken, string NewPassword);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/auth/me
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `refreshSession()` (เรียกตอนเปิดเว็บ, หลัง Login/Register/Logout)
    // ข้อมูลที่รับ (Input): คุกกี้ยืนยันตัวตน `OldThingsAuth` (ถ้ามี) ที่เบราว์เซอร์ส่งมาอัตโนมัติ
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { authenticated, isDevelopment, user, settings }
    //   (และหากบทบาทใน DB เปลี่ยน เช่น Buyer ได้รับอนุมัติเป็น Seller จะออก Set-Cookie `OldThingsAuth` ใหม่ให้อัตโนมัติ)
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // ดึงการตั้งค่าระบบล่าสุด (ส่วนลดหมวดเดียวกัน, ค่าธรรมเนียม, ขั้นต่ำการบิด)
        var settings = await lifecycle.GetSettingsAsync();
        // ดึงข้อมูลผู้ใช้ที่ล็อกอินอยู่และยังไม่ถูกระงับจากฐานข้อมูล
        var user = await GetCurrentActiveUserAsync();
        if (user is not null)
        {
            // ตรวจสอบว่า Role ในคุกกี้ตรงกับ Role ล่าสุดในฐานข้อมูลหรือไม่ (กรณีแอดมินเพิ่งอนุมัติเลื่อนขั้นเป็น Seller)
            var cookieRole = User.FindFirstValue(ClaimTypes.Role);
            if (!string.Equals(cookieRole, user.Role, StringComparison.Ordinal))
            {
                // อัปเดตคุกกี้ใหม่ให้มี Claim Role ตรงกับฐานข้อมูลทันทีโดยไม่ต้องบังคับออกแล้วเข้าใหม่
                await SignInAsync(user);
            }
        }
        // ส่งสถานะการล็อกอิน ข้อมูลผู้ใช้ (หรือ null) และกติการะบบกลับไปยังหน้าบ้าน
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

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: GET /api/auth/demo-accounts
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟังก์ชัน `openAuthModal()` (แสดงการ์ดบัญชีทดลอง 4 บทบาทในหน้าต่างเข้าสู่ระบบ)
    // ข้อมูลที่รับ (Input): ไม่มีพารามิเตอร์
    // ข้อมูลที่ส่งกลับ (Output):
    //   - โหมด Development: HTTP 200 JSON Array ของบัญชีทดลอง 4 บทบาท [{ role, label, email, name, note }, ...]
    //   - โหมด Production: HTTP 404 JSON { message } เพื่อปิดปุ่มล็อกอินทดลองบนระบบจริง
    [HttpGet("demo-accounts")]
    public IActionResult DemoAccounts()
    {
        // ป้องกันไม่ให้เปิดเผยบัญชีทดลองในสภาพแวดล้อมใช้งานจริง (Production)
        if (!env.IsDevelopment())
            return NotFound(new { message = "โหมดบัญชีทดลองเปิดเฉพาะสภาพแวดล้อมพัฒนา (Development) เท่านั้น" });

        // ส่งรายชื่อบัญชีทดลองทั้ง 4 บทบาท (Buyer, Seller, Admin, Finance) ให้หน้าบ้านนำไปเรนเดอร์เป็นการ์ดกดคลิกเดียว
        return Ok(new[]
        {
            new { role = Roles.Buyer, label = "ผู้ประมูล (Buyer)", email = "buyer@oldthings.th", name = "คุณพิมพ์ชนก", note = "มีส่วนลดซื้อหมวดเดียวกัน 10% พร้อมทดสอบชำระเงิน" },
            new { role = Roles.Seller, label = "ผู้ขาย (Seller)", email = "seller@oldthings.th", name = "คุณชาญชัย", note = "อนุมัติแล้ว ลงสินค้า นัดรับ ประมูลซื้อของอื่นได้ด้วย" },
            new { role = Roles.Admin, label = "แอดมิน (Admin)", email = "admin@oldthings.th", name = "แอดมินสุวิจักขณ์", note = "อนุมัติผู้ขาย/สินค้า ระงับบัญชี หมวดหมู่ ข้อพิพาท Audit Log" },
            new { role = Roles.Finance, label = "การเงิน (Finance)", email = "finance@oldthings.th", name = "ฝ่ายการเงินนิภา", note = "ตรวจสลิป ปล่อยเงิน คืนเงิน ตั้งกติกา รายงาน CSV" }
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/login
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#loginForm` (submit) และปุ่มการ์ดบัญชีทดลอง `[data-demo-email]` (click)
    // ข้อมูลที่รับ (Input): JSON Request Body (`LoginRequest`: { email, password })
    // ข้อมูลที่ส่งกลับ (Output):
    //   - HTTP 200 JSON { message, user } พร้อม Header `Set-Cookie: OldThingsAuth=...` (อายุ 7 วัน)
    //   - HTTP 400 (กรอกไม่ครบ), 401 (อีเมล/รหัสผ่านผิด), 403 (บัญชีถูกระงับ), 429 (ติด Rate Limit หรือถูกล็อกชั่วคราว 5 นาที)
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // จัดรูปอีเมลเป็นตัวพิมพ์เล็กและตัดช่องว่างหน้า-หลังเพื่อป้องกันการเทียบพลาด
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "กรุณากรอกอีเมลและรหัสผ่าน" });

        // ค้นหาผู้ใช้จากฐานข้อมูลด้วยอีเมล (ไม่สนใจตัวพิมพ์เล็ก-ใหญ่)
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null)
            return Unauthorized(new { message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง" });

        // ตรวจสอบว่าบัญชีกำลังอยู่ในช่วงถูกล็อกชั่วคราว (Lockout) หรือไม่
        var now = DateTime.UtcNow;
        if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > now)
        {
            // คำนวณจำนวนนาทีที่เหลือ (ปัดขึ้นอย่างน้อย 1 นาที)
            var remainMin = Math.Max(1, (int)Math.Ceiling((user.LockoutEndAt.Value - now).TotalMinutes));
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"ลองเข้าสู่ระบบผิดหลายครั้ง บัญชีถูกล็อกชั่วคราวอีกประมาณ {remainMin} นาที"
            });
        }

        // ตรวจสอบความถูกต้องของรหัสผ่านที่กรอกเทียบกับ PasswordHash ในฐานข้อมูล
        if (!PasswordService.Verify(req.Password, user.PasswordHash))
        {
            // นับจำนวนครั้งที่กรอกรหัสผ่านผิดเพิ่มขึ้น 1
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLoginAttempts)
            {
                // ผิดครบ 5 ครั้ง -> ตั้งเวลาล็อกบัญชี 5 นาที และรีเซ็ตตัวนับ
                user.LockoutEndAt = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
                await Db.SaveChangesAsync();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = $"รหัสผ่านผิดครบ {MaxFailedLoginAttempts} ครั้ง บัญชีถูกล็อกชั่วคราว {(int)LockoutDuration.TotalMinutes} นาที"
                });
            }

            // แจ้งจำนวนครั้งที่เหลือให้ผู้ใช้ทราบและบันทึกลงฐานข้อมูล
            var remainingTries = MaxFailedLoginAttempts - user.FailedLoginCount;
            await Db.SaveChangesAsync();
            return Unauthorized(new { message = $"อีเมลหรือรหัสผ่านไม่ถูกต้อง (เหลือโอกาสลองอีก {remainingTries} ครั้ง)" });
        }

        // ปฏิเสธการเข้าสู่ระบบหากบัญชีถูกแอดมินระงับการใช้งาน
        if (user.IsSuspended)
            return StatusCode(403, new { message = $"บัญชีนี้ถูกระงับการใช้งาน{(string.IsNullOrWhiteSpace(user.SuspendReason) ? "" : $": {user.SuspendReason}")}" });

        // เมื่อล็อกอินสำเร็จ ให้ล้างประวัติการกรอกผิดและสถานะล็อกชั่วคราว
        if (user.FailedLoginCount > 0 || user.LockoutEndAt.HasValue)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndAt = null;
            await Db.SaveChangesAsync();
        }

        // ออกคุกกี้ยืนยันตัวตน `OldThingsAuth` ส่งกลับไปเก็บที่เบราว์เซอร์
        await SignInAsync(user);
        return Ok(new { message = "เข้าสู่ระบบสำเร็จ", user = MapUserSummary(user) });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/register
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#registerForm` (submit)
    // ข้อมูลที่รับ (Input): JSON Request Body (`RegisterRequest`: { displayName, email, password, role, shopName?, phone?, bankAccount?, sellerBio? })
    // ข้อมูลที่ส่งกลับ (Output):
    //   - HTTP 200 JSON { message, user } พร้อม Header `Set-Cookie: OldThingsAuth=...` (ล็อกอินให้ทันทีหลังสมัคร)
    //   - HTTP 400 (ข้อมูลไม่ครบ/รูปแบบไม่ถูกต้อง), HTTP 409 (อีเมลซ้ำในระบบ)
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        // ทำความสะอาดข้อมูลอินพุต และจำกัดบทบาทที่สมัครเองได้เฉพาะ Buyer หรือ Seller (กันสมัครเป็น Admin/Finance)
        var displayName = (req.DisplayName ?? "").Trim();
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var password = req.Password ?? "";
        var role = req.Role == Roles.Seller ? Roles.Seller : Roles.Buyer;

        // ตรวจสอบความถูกต้องของชื่อ อีเมล และความยาวรหัสผ่านขั้นต่ำ
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            return BadRequest(new { message = "กรุณาระบุชื่อที่แสดงอย่างน้อย 2 ตัวอักษร" });
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "รูปแบบอีเมลไม่ถูกต้อง" });
        if (password.Length < 6)
            return BadRequest(new { message = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร" });

        // ตรวจสอบว่าอีเมลนี้ถูกใช้สมัครไปแล้วหรือไม่
        var exists = await Db.Users.AnyAsync(u => u.Email.ToLower() == email);
        if (exists)
            return Conflict(new { message = "อีเมลนี้ถูกใช้งานแล้ว" });

        // หากเลือกสมัครเป็นผู้ขาย (Seller) บังคับต้องกรอกข้อมูลร้าน เบอร์โทร และบัญชีรับเงิน
        if (role == Roles.Seller)
        {
            if (string.IsNullOrWhiteSpace(req.ShopName) || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.BankAccount))
                return BadRequest(new { message = "การสมัครเป็นผู้ขายต้องระบุชื่อร้าน เบอร์โทรศัพท์ และบัญชีรับเงิน" });
        }

        // สร้างเอนทิตี User ใหม่พร้อมแฮชรหัสผ่าน (ผู้ขายจะได้สถานะ Pending เพื่อรอแอดมินอนุมัติ)
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

        // บันทึกผู้ใช้ลงฐานข้อมูลและออกคุกกี้เข้าสู่ระบบให้อัตโนมัติ
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        await SignInAsync(user);

        var msg = role == Roles.Seller
            ? "สมัครสมาชิกผู้ขายสำเร็จ กรุณารอแอดมินอนุมัติก่อนลงขายสินค้า"
            : "สมัครสมาชิกผู้ประมูลสำเร็จ";
        return Ok(new { message = msg, user = MapUserSummary(user) });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/change-password (ต้องล็อกอินก่อน: [Authorize])
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#changePasswordForm` (เปิดจากปุ่ม `[data-open-change-pw]` บนแถบด้านบน)
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth` + JSON Request Body (`ChangePasswordRequest`: { currentPassword, newPassword })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message }, HTTP 400 (รหัสเดิมผิด/รหัสใหม่สั้นเกิน), HTTP 401 (ยังไม่ล็อกอิน)
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        // ดึงบัญชีผู้ใช้ปัจจุบันที่ล็อกอินอยู่
        var user = await GetCurrentActiveUserAsync();
        if (user is null) return Unauthorized();

        // ตรวจสอบความครบถ้วน ความยาวขั้นต่ำ และยืนยันว่ารหัสผ่านเดิมถูกต้อง
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "กรุณากรอกรหัสผ่านเดิมและรหัสผ่านใหม่" });
        if (req.NewPassword.Length < 6)
            return BadRequest(new { message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร" });
        if (!PasswordService.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "รหัสผ่านเดิมไม่ถูกต้อง" });

        // อัปเดตแฮชรหัสผ่านใหม่ลงฐานข้อมูล
        user.PasswordHash = PasswordService.Hash(req.NewPassword);
        await Db.SaveChangesAsync();
        return Ok(new { message = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/forgot-password (จำกัดอัตราการเรียกด้วย Rate Limiter "login")
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#forgotPasswordForm` (แท็บลืมรหัสผ่านในหน้าต่างเข้าสู่ระบบ)
    // ข้อมูลที่รับ (Input): JSON Request Body (`ForgotPasswordRequest`: { email })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message, devResetCode }
    //   (ส่ง `devResetCode` เฉพาะโหมด Development เพื่อทดสอบได้โดยไม่ต้องส่งอีเมลจริง; โหมด Production เป็น null)
    [EnableRateLimiting("login")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "กรุณากรอกอีเมลให้ถูกต้อง" });

        // ค้นหาผู้ใช้จากอีเมล (ถ้าไม่พบหรือถูกระงับ จะยังคงตอบ 200 ข้อความกลางๆ เพื่อป้องกันการเดาอีเมลในระบบ)
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        string? devCode = null;
        if (user is not null && !user.IsSuspended)
        {
            // สุ่มรหัสรีเซ็ต 6 หลักแบบปลอดภัยทางวิทยาการเข้ารหัส (Cryptographically Secure) มีอายุ 15 นาที
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetToken = code;
            user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await Db.SaveChangesAsync();
            // แสดงรหัสใน Response เฉพาะสภาพแวดล้อม Development เพื่อให้ทดสอบโฟลว์รีเซ็ตได้ทันที
            if (env.IsDevelopment()) devCode = code;
        }

        return Ok(new
        {
            message = "หากมีอีเมลนี้ในระบบ รหัสรีเซ็ต 6 หลักจะถูกสร้างและใช้งานได้ภายใน 15 นาที",
            devResetCode = devCode
        });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/reset-password (จำกัดอัตราการเรียกด้วย Rate Limiter "login")
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ฟอร์ม `#resetPasswordForm` (แท็บลืมรหัสผ่านในหน้าต่างเข้าสู่ระบบ)
    // ข้อมูลที่รับ (Input): JSON Request Body (`ResetPasswordRequest`: { email, resetToken, newPassword })
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message } หรือ HTTP 400 (รหัสรีเซ็ตไม่ถูกต้อง/หมดอายุ/รหัสใหม่สั้นเกิน)
    [EnableRateLimiting("login")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var token = (req.ResetToken ?? "").Trim();
        var newPassword = req.NewPassword ?? "";

        // ตรวจสอบข้อมูลเบื้องต้นก่อนค้นหาฐานข้อมูล
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "กรุณากรอกอีเมลและรหัสรีเซ็ต 6 หลัก" });
        if (newPassword.Length < 6)
            return BadRequest(new { message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร" });

        // ตรวจสอบว่าผู้ใช้มีอยู่จริง รหัสรีเซ็ตตรงกัน และยังไม่หมดอายุ 15 นาที
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null
            || string.IsNullOrWhiteSpace(user.PasswordResetToken)
            || !string.Equals(user.PasswordResetToken, token, StringComparison.Ordinal)
            || !user.PasswordResetExpiresAt.HasValue
            || user.PasswordResetExpiresAt.Value < DateTime.UtcNow)
        {
            return BadRequest(new { message = "รหัสรีเซ็ตไม่ถูกต้องหรือหมดอายุแล้ว กรุณาขอรหัสใหม่" });
        }

        // ตั้งรหัสผ่านใหม่ ล้างโทเค็นรีเซ็ตแบบใช้ครั้งเดียว และปลดล็อกสถานะล็อกชั่วคราว (ถ้ามี)
        user.PasswordHash = PasswordService.Hash(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        await Db.SaveChangesAsync();

        return Ok(new { message = "ตั้งรหัสผ่านใหม่เรียบร้อยแล้ว สามารถเข้าสู่ระบบได้ทันที" });
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Endpoint: POST /api/auth/logout
    // หน้าบ้านที่เรียกใช้: `wwwroot/js/site.js` -> ปุ่ม `#logoutBtn` (click) บนแถบด้านบน
    // ข้อมูลที่รับ (Input): คุกกี้ `OldThingsAuth`
    // ข้อมูลที่ส่งกลับ (Output): HTTP 200 JSON { message } พร้อมสั่งลบคุกกี้ `OldThingsAuth` ในเบราว์เซอร์
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // ลบคุกกี้ยืนยันตัวตนออกจาก HttpContext (เบราว์เซอร์จะล้างคุกกี้อัตโนมัติ)
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "ออกจากระบบแล้ว" });
    }

    // เมธอดช่วยภายใน (Helper): สร้าง ClaimsIdentity และออกคุกกี้ `OldThingsAuth` (Persistent อายุ 7 วัน)
    // เก็บ Claim หลัก 4 ค่า ได้แก่ User Id, DisplayName, Email และ Role เพื่อใช้กับ [Authorize] และ ApiBaseController
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
