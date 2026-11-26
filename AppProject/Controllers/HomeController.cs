// ============================================================================
// ไฟล์: Controllers/HomeController.cs (ฝั่งหลังบ้าน - MVC Controller สำหรับเสิร์ฟหน้าเว็บ)
// หน้าที่: เป็นจุดส่งมอบหน้า HTML หลัก (Single-Page-style Shell) ให้กับเบราว์เซอร์ของผู้ใช้
//         และหน้าแจ้งข้อผิดพลาด (Error View) เมื่อเกิดข้อผิดพลาดระดับเซิร์ฟเวอร์
// เชื่อมต่อกับ:
//   - Program.cs (MapControllerRoute ค่าเริ่มต้น "{controller=Home}/{action=Index}/{id?}")
//   - Views/Home/Index.cshtml และ Views/Shared/_Layout.cshtml (โครงหน้าบ้านทั้งหมด)
// ============================================================================

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AppProject.Models;

namespace AppProject.Controllers;

public class HomeController : Controller
{
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Route: GET / หรือ GET /Home/Index
    // เมื่อผู้ใช้เข้าเว็บไซต์ครั้งแรก หลังบ้านจะเรนเดอร์ไฟล์ Views/Home/Index.cshtml (ครอบด้วย _Layout.cshtml)
    // ส่ง HTML พร้อมลิงก์โหลด site.css และ site.js กลับไปให้เบราว์เซอร์
    // จากนั้นหน้าบ้าน (site.js) จะเป็นคนยิง API (/api/*) มาขอข้อมูล JSON จากหลังบ้านต่อไป
    public IActionResult Index()
    {
        return View();
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    // Route: GET /Home/Error (ถูกเรียกอัตโนมัติโดย app.UseExceptionHandler ในโหมด Production)
    // ส่งไฟล์ Views/Shared/Error.cshtml พร้อม RequestId สำหรับใช้อ้างอิงหาสาเหตุปัญหา
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
