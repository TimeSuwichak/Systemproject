// ============================================================================
// ไฟล์: Program.cs (ฝั่งหลังบ้าน - จุดเริ่มต้นของเว็บแอปพลิเคชัน ASP.NET Core)
// หน้าที่: ตั้งค่าบริการ (Services), ฐานข้อมูล SQLite, ระบบยืนยันตัวตนด้วย Cookie,
//         ระบบจำกัดอัตราการยิงคำขอ (Rate Limiting), งานเบื้องหลัง (Background Service),
//         และเส้นทาง (Routing) ทั้งหมดที่เป็นจุดเชื่อมต่อระหว่างหน้าบ้านกับหลังบ้าน
// เชื่อมต่อกับ:
//   - หน้าบ้าน (Frontend): เสิร์ฟไฟล์หน้าเว็บผ่าน HomeController (/) และไฟล์ใน wwwroot/ (CSS/JS/รูปภาพ)
//   - หลังบ้าน (Backend API): เปิดเส้นทาง /api/* ให้ Controller ต่างๆ รับคำขอ JSON จาก wwwroot/js/site.js
// ============================================================================

using System.Threading.RateLimiting;
using AppProject.Data;
using AppProject.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// สร้าง WebApplicationBuilder เพื่อเริ่มตั้งค่าบริการและคอนฟิกของแอป
var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// 1. ตั้งค่า Data Protection (ใช้เข้ารหัส/ถอดรหัส Cookie ยืนยันตัวตน)
// ----------------------------------------------------------------------------
// กำหนดโฟลเดอร์เก็บกุญแจเข้ารหัสในเครื่อง เพื่อให้รีสตาร์ทเซิร์ฟเวอร์แล้ว Cookie ผู้ใช้ยังไม่หลุด
var keysDir = Path.Combine(builder.Environment.ContentRootPath, ".dataprotection-keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir));

// ----------------------------------------------------------------------------
// 2. เชื่อมต่อฐานข้อมูล SQLite ผ่าน Entity Framework Core
// ----------------------------------------------------------------------------
// อ่าน ConnectionString จาก appsettings.json ถ้าไม่มีให้ใช้ไฟล์ auction.db เป็นค่าเริ่มต้น
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=auction.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

// ----------------------------------------------------------------------------
// 3. ลงทะเบียน Business Logic Service และ Background Service
// ----------------------------------------------------------------------------
// AuctionLifecycleService (Scoped): บริการคำนวณราคา ปิดประมูล ยกเลิกออเดอร์หมดเวลา และส่งต่อรองชนะเลิศ
builder.Services.AddScoped<AuctionLifecycleService>();
// AuctionClosingBackgroundService (HostedService): งานเบื้องหลังที่รันอัตโนมัติเป็นรอบๆ เพื่อปิดประมูลที่หมดเวลา
builder.Services.AddHostedService<AuctionClosingBackgroundService>();

// ----------------------------------------------------------------------------
// 4. ตั้งค่า Rate Limiter (ป้องกันการยิง Login รัวๆ / Brute-force)
// ----------------------------------------------------------------------------
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
// เมื่อหน้าบ้าน (site.js) เรียก POST /api/auth/login เกินโควตา (10 ครั้ง/นาที ต่อ IP)
// หลังบ้านจะตอบกลับ HTTP 429 พร้อม JSON { message: "..." } ให้หน้าบ้านแสดง Toast แจ้งเตือน
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "คุณพยายามทำรายการถี่เกินไป กรุณารอสักครู่แล้วลองใหม่" }, ct);
    };
    // นโยบาย "login" ใช้กับ [EnableRateLimiting("login")] ใน AuthController
    options.AddPolicy("login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,                // อนุญาตสูงสุด 10 ครั้ง
            Window = TimeSpan.FromMinutes(1), // ภายในกรอบเวลา 1 นาที
            QueueLimit = 0                    // ไม่ต่อคิว ถ้าเกินให้ปฏิเสธทันที
        });
    });
});

// ----------------------------------------------------------------------------
// 5. ตั้งค่าระบบยืนยันตัวตนด้วย Cookie Authentication
// ----------------------------------------------------------------------------
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] (กลไกการยืนยันตัวตนระหว่าง Browser กับ API)
// - หลังบ้านสร้าง Cookie ชื่อ "OldThingsAuth" (HttpOnly) ส่งให้เบราว์เซอร์ตอน Login/Register สำเร็จ
// - หน้าบ้าน (site.js) ใช้ fetch(..., { credentials: "same-origin" }) แนบ Cookie นี้กลับมากับทุกคำขอ /api/*
// - หากยังไม่ล็อกอินหรือไม่มีสิทธิ์ และคำขอขึ้นต้นด้วย /api หลังบ้านจะส่ง JSON (401/403) แทนการ Redirect
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "OldThingsAuth";         // ชื่อ Cookie ที่เก็บ session ผู้ใช้
        options.Cookie.HttpOnly = true;                 // ป้องกัน JavaScript อ่าน Cookie โดยตรง (กัน XSS)
        options.Cookie.SameSite = SameSiteMode.Lax;     // ป้องกัน CSRF เบื้องต้น
        // กรณีผู้ใช้ยังไม่ล็อกอินแต่เรียก API ที่ติด [Authorize]
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return ctx.Response.WriteAsJsonAsync(new { message = "กรุณาเข้าสู่ระบบก่อนดำเนินการ" });
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        // กรณีผู้ใช้ล็อกอินแล้วแต่ Role ไม่มีสิทธิ์เรียก API นั้น (เช่น Buyer พยายามเรียก /api/admin/*)
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ctx.Response.WriteAsJsonAsync(new { message = "คุณไม่มีสิทธิ์เข้าถึงส่วนนี้" });
            }
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// เปิดใช้งานระบบตรวจสอบสิทธิ์ (Role-based Authorization) และ MVC (Controllers + Razor Views)
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

// สร้างอินสแตนซ์แอปพลิเคชันพร้อมใช้งาน
var app = builder.Build();

// ----------------------------------------------------------------------------
// 6. รันการเตรียมฐานข้อมูล (Migration + Seed ข้อมูลเริ่มต้น) ตอนเปิดเซิร์ฟเวอร์
// ----------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db); // สร้างตารางและเติมบัญชีตัวอย่าง/หมวดหมู่/สินค้าตัวอย่างถ้ายังไม่มี
}

// ในโหมด Production ให้ส่งหน้า Error สวยงามและเปิด HSTS เพื่อความปลอดภัย
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ----------------------------------------------------------------------------
// 7. เตรียมโฟลเดอร์เก็บไฟล์อัปโหลดของหน้าบ้าน (สลิปโอนเงิน และรูปสินค้า)
// ----------------------------------------------------------------------------
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
// เมื่อผู้ขายอัปโหลดรูปสินค้า (POST /api/seller/upload-image) หรือผู้ซื้อส่งสลิป (POST /api/buyer/orders/{id}/pay)
// หลังบ้านจะบันทึกไฟล์ลงโฟลเดอร์ wwwroot/uploads/* และส่ง URL กลับไปให้หน้าบ้านแสดงในแท็ก <img>
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "slips"));
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "products"));

// ----------------------------------------------------------------------------
// 8. กำหนดลำดับ Middleware Pipeline (ลำดับสำคัญมาก!)
// ----------------------------------------------------------------------------
app.UseHttpsRedirection();  // บังคับเปลี่ยน HTTP เป็น HTTPS (เมื่อเปิดใช้ HTTPS)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] เสิร์ฟไฟล์คงที่จากโฟลเดอร์ wwwroot (site.css, site.js, รูปใน uploads/)
app.UseStaticFiles();
app.UseRouting();           // จับคู่ URL ขาเข้ากับ Controller/Action

app.UseRateLimiter();       // ตรวจสอบโควตาการยิงคำขอก่อนถึง Controller
app.UseAuthentication();    // แกะ Cookie "OldThingsAuth" ออกมาเป็น ClaimsPrincipal (HttpContext.User)
app.UseAuthorization();     // ตรวจสอบสิทธิ์ [Authorize(Roles = ...)] ของแต่ละ API

// แมปไฟล์ Static Asset รุ่นใหม่ของ ASP.NET Core (.NET 9/10)
app.MapStaticAssets();

// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] (ทางเข้าหลักของเว็บ)
// เมื่อผู้ใช้เปิด URL "/" ในเบราว์เซอร์ จะถูกส่งไปที่ HomeController.Index()
// เพื่อเรนเดอร์ไฟล์ Views/Home/Index.cshtml (โครงหน้าเว็บหลัก + โหลด site.js และ site.css)
// ส่วน API Controllers ที่ติด [Route("api/...")] จะถูกจับคู่ผ่าน Attribute Routing โดยอัตโนมัติ
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// เริ่มรันเว็บเซิร์ฟเวอร์และรอรับ HTTP Request จากหน้าบ้าน
app.Run();
