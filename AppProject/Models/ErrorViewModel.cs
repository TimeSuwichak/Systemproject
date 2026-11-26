// ============================================================================
// ไฟล์: Models/ErrorViewModel.cs (ViewModel สำหรับส่งข้อมูลไปหน้าแสดงข้อผิดพลาด)
// หน้าที่: ส่งรหัสอ้างอิงคำขอ (RequestId) จาก HomeController.Error() ไปแสดงผลใน Views/Shared/Error.cshtml
// ============================================================================

namespace AppProject.Models;

public class ErrorViewModel
{
    // รหัสอ้างอิง HTTP Request ปัจจุบัน ใช้ช่วยตามหา Log ข้อผิดพลาดบนเซิร์ฟเวอร์
    public string? RequestId { get; set; }

    // คืนค่า true เมื่อมี RequestId เพื่อให้ Error.cshtml แสดงรหัสอ้างอิงบนหน้าจอ
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
