// ============================================================================
// 📄 ชื่อไฟล์: AppProject/Models/Entities.cs
// 🏷️ บทบาท: ฝั่งหลังบ้าน (Backend) — ชั้นโมเดลข้อมูล (Data Models / Database Entities) และค่าคงที่สถานะ (Status Constants)
// 📝 หน้าที่โดยรวม:
//    กำหนดโครงสร้างตารางฐานข้อมูลทั้งหมด (12 ตาราง) ของระบบประมูล "ของเก่าเล่าเรื่อง" (OldThings)
//    รวมถึงคลาสค่าคงที่ (Constants) สำหรับบทบาทผู้ใช้และสถานะต่าง ๆ ในระบบประมูล ชำระเงิน นัดรับ และข้อพิพาท
// 🔄 การเชื่อมต่อกับส่วนอื่นในระบบ:
//    - ฐานข้อมูล: ถูกนำไปแมปเป็นตารางใน SQLite ผ่าน AppProject/Data/AppDbContext.cs และเติมข้อมูลเริ่มต้นใน DbSeeder.cs
//    - ตรรกะธุรกิจ (Business Logic): ถูกใช้งานใน AppProject/Services/AuctionLifecycleService.cs
//    - ฝั่ง API (Controllers): ทุก Controller ใน AppProject/Controllers/*.cs ดึงข้อมูลจาก Entity เหล่านี้และแปลงเป็น JSON ส่งกลับหน้าบ้าน
//    - ฝั่งหน้าบ้าน (Frontend): ฟิลด์และค่าคงที่สถานะในไฟล์นี้ถูกแปลงเป็น JSON (camelCase) เพื่อให้ wwwroot/js/site.js แสดงผลและเช็กเงื่อนไข UI
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace AppProject.Models;

// ----------------------------------------------------------------------------
// กลุ่มคลาสค่าคงที่ (Status & Role Constants)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ค่าข้อความในคลาสค่าคงที่ด้านล่างถูกส่งผ่าน JSON Response
//    จากทุก API Controller ไปยัง wwwroot/js/site.js เพื่อใช้แสดงป้ายสถานะ (Badge), ปุ่มดำเนินการ และสิทธิ์เมนู
// ----------------------------------------------------------------------------

// บทบาทของผู้ใช้งานในระบบ (ใช้ใน User.Role และ Cookie Claims สำหรับตรวจสิทธิ์ Authorize)
public static class Roles
{
    public const string Buyer = "Buyer";     // ผู้ซื้อ / ผู้เข้าร่วมประมูล (บทบาทเริ่มต้นเมื่อสมัครสมาชิก)
    public const string Seller = "Seller";   // ผู้ขาย (สามารถลงประกาศสินค้าประมูลได้เมื่อ SellerStatus = Approved)
    public const string Admin = "Admin";     // ผู้ดูแลระบบ (อนุมัติผู้ขาย/สินค้า จัดการหมวดหมู่ ระงับบัญชี และตรวจสอบข้อพิพาท)
    public const string Finance = "Finance"; // ฝ่ายการเงิน (ตรวจสอบสลิป ปล่อยเงิน Escrow คืนเงิน และตั้งค่าอัตราส่วนลด/ค่าธรรมเนียม)
}

// สถานะการสมัครเป็นผู้ขายของบัญชีผู้ใช้ (ใช้ใน User.SellerStatus)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] เปลี่ยนแปลงผ่าน POST /api/seller/apply และ POST /api/admin/sellers/{userId}/review
public static class SellerStatuses
{
    public const string None = "None";         // ยังไม่ได้สมัครเป็นผู้ขาย
    public const string Pending = "Pending";   // ส่งคำขอสมัครผู้ขายแล้ว รอแอดมินพิจารณา
    public const string Approved = "Approved"; // แอดมินอนุมัติแล้ว สามารถลงขายสินค้าได้
    public const string Rejected = "Rejected"; // คำขอถูกปฏิเสธ (ดูเหตุผลใน User.SellerRejectReason)
}

// สถานะของรายการสินค้าประมูล (ใช้ใน AuctionItem.Status)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] หน้าบ้านอ่านผ่าน GET /api/auctions และเปลี่ยนสถานะผ่าน POST /api/admin/auctions/{itemId}/review หรือ AuctionLifecycleService
public static class AuctionStatuses
{
    public const string Pending = "Pending";   // ผู้ขายลงสินค้าใหม่ รอแอดมินตรวจสอบอนุมัติก่อนขึ้นหน้าร้าน
    public const string Approved = "Approved"; // แอดมินอนุมัติแล้ว กำลังเปิดให้ประมูล (หรือรอถึงเวลา StartAt)
    public const string Rejected = "Rejected"; // แอดมินไม่อนุมัติ (ดูเหตุผลใน AuctionItem.RejectReason)
    public const string Closed = "Closed";     // ปิดประมูลแล้ว (หมดเวลา EndAt ถูกปิดอัตโนมัติโดย AuctionLifecycleService)
}

// วิธีการชำระเงินของคำสั่งซื้อหลังชนะประมูล (ใช้ใน OrderPayment.PaymentMethod)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ผู้ซื้อเลือกตอนแจ้งชำระเงินผ่าน POST /api/buyer/orders/{orderId}/pay (wwwroot/js/site.js)
public static class PaymentMethods
{
    public const string PlatformEscrow = "PlatformEscrow"; // โอนผ่านบัญชีกลางของแพลตฟอร์ม (Escrow) ฝ่ายการเงินตรวจสลิปและถือเงินไว้ก่อนปล่อยให้ผู้ขาย
    public const string DirectToSeller = "DirectToSeller"; // โอนตรงให้ผู้ขาย ผู้ขายเป็นผู้ตรวจสอบสลิปเอง
}

// สถานะการชำระเงินของคำสั่งซื้อ (ใช้ใน OrderPayment.PaymentStatus)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] อัปเดตผ่าน POST /api/buyer/orders/{orderId}/pay, POST /api/finance/orders/{orderId}/verify-payment, POST /api/seller/orders/{orderId}/verify-payment
public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";                           // ยังไม่ชำระเงิน (มีกำหนดเวลา PaymentDueAt 48 ชม.)
    public const string PendingVerification = "PendingVerification"; // ผู้ซื้ออัปโหลดสลิปแล้ว รอฝ่ายการเงินหรือผู้ขายตรวจสอบ
    public const string Paid = "Paid";                               // ชำระเงินและยืนยันสลิปเรียบร้อยแล้ว
    public const string Refunded = "Refunded";                       // คืนเงินให้ผู้ซื้อแล้ว (ดำเนินการโดยฝ่ายการเงิน)
    public const string Cancelled = "Cancelled";                     // ถูกยกเลิก (เช่น หมดเวลาชำระเงิน 48 ชม. ระบบยกเลิกอัตโนมัติ)
}

// สถานะการนัดรับและส่งมอบสินค้า (ใช้ใน OrderPayment.MeetupStatus)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ผู้ขายอัปเดตผ่าน POST /api/seller/orders/{orderId}/meetup และ /deliver; ผู้ซื้อยืนยันผ่าน POST /api/buyer/orders/{orderId}/confirm-received
public static class MeetupStatuses
{
    public const string NotScheduled = "NotScheduled"; // ยังไม่ได้กำหนดวัน/สถานที่นัดรับ
    public const string Scheduled = "Scheduled";       // ผู้ขายกำหนดวันเวลาและสถานที่นัดรับแล้ว
    public const string Delivered = "Delivered";       // ผู้ขายส่งมอบสินค้าแล้ว รอผู้ซื้อกดยืนยันรับของ (หรือรอครบ 3 วันเพื่อปล่อยเงิน Escrow)
    public const string Completed = "Completed";       // ผู้ซื้อกดยืนยันได้รับสินค้าเรียบร้อยแล้ว (ปิดงานส่งมอบสมบูรณ์)
}

// สถานะการโอนเงิน/ปล่อยเงินให้ผู้ขาย (ใช้ใน OrderPayment.PayoutStatus)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ฝ่ายการเงินจัดการผ่าน POST /api/finance/orders/{orderId}/release และ /refund; หน้าบ้านแสดงผลในหน้าการเงินและยอดขายผู้ขาย
public static class PayoutStatuses
{
    public const string Pending = "Pending";             // รอปล่อยเงินจากบัญชีกลาง Escrow ให้ผู้ขาย
    public const string Released = "Released";           // ฝ่ายการเงินปล่อยเงินให้ผู้ขายเรียบร้อยแล้ว
    public const string DirectSettled = "DirectSettled"; // ปิดยอดแบบโอนตรงให้ผู้ขาย (ไม่ต้องผ่านการปล่อยเงินจาก Escrow)
    public const string Refunded = "Refunded";           // คืนเงินกลับให้ผู้ซื้อแล้ว
    public const string Cancelled = "Cancelled";         // ยกเลิกการจ่ายเงิน (เนื่องจากคำสั่งซื้อถูกยกเลิก)
}

// สถานะของข้อพิพาท/คำร้องเรียน (ใช้ใน Dispute.Status)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] สร้างผ่าน POST /api/chat/conversations/{id}/dispute และอัปเดตผ่าน POST /api/admin/disputes/{disputeId}/update
public static class DisputeStatuses
{
    public const string Open = "Open";                   // เปิดข้อพิพาทใหม่ รอแอดมินรับเรื่อง (จะบล็อกการปล่อยเงิน Escrow ชั่วคราว)
    public const string Investigating = "Investigating"; // แอดมินกำลังตรวจสอบแชทและหลักฐาน (ยังบล็อกการปล่อยเงิน Escrow)
    public const string Resolved = "Resolved";           // แก้ไขข้อพิพาทเสร็จสิ้นแล้ว
}

// ============================================================================
// กลุ่มคลาส Entity (โครงสร้างตารางในฐานข้อมูล SQLite)
// ============================================================================

// ตาราง Users: เก็บข้อมูลบัญชีผู้ใช้งานทุกบทบาท (ผู้ซื้อ, ผู้ขาย, แอดมิน, ฝ่ายการเงิน)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ข้อมูลผู้ใช้ถูกส่งกลับเป็น JSON (เฉพาะฟิลด์ปลอดภัย ไม่ส่ง PasswordHash)
//    ผ่าน GET /api/auth/me, POST /api/auth/login, GET /api/admin/dashboard ไปยัง wwwroot/js/site.js
public class User
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของผู้ใช้

    // --- ข้อมูลบัญชีและการยืนยันตัวตน (Authentication & Profile) ---
    [Required, MaxLength(80)]
    public string DisplayName { get; set; } = string.Empty; // ชื่อที่แสดงในระบบและหน้าแชท

    [Required, MaxLength(120)]
    public string Email { get; set; } = string.Empty; // อีเมลใช้ล็อกอิน (ตั้ง Unique Index ใน AppDbContext)

    [Required]
    public string PasswordHash { get; set; } = string.Empty; // รหัสผ่านที่เข้ารหัสแบบแฮชแล้ว (ผ่าน PasswordService ห้ามส่งออกไปหน้าบ้าน)

    [Required, MaxLength(20)]
    public string Role { get; set; } = Roles.Buyer; // บทบาทผู้ใช้ (Buyer / Seller / Admin / Finance)

    // --- สถานะการระงับบัญชี (Moderation) ---
    public bool IsSuspended { get; set; } // true หากถูกแอดมินระงับบัญชี (จะล็อกอินและใช้งานไม่ได้)
    [MaxLength(300)]
    public string? SuspendReason { get; set; } // เหตุผลที่ถูกระงับบัญชี

    // --- ข้อมูลร้านค้าและคำขอสมัครเป็นผู้ขาย (Seller Onboarding) ---
    [Required, MaxLength(20)]
    public string SellerStatus { get; set; } = SellerStatuses.None; // สถานะการสมัครผู้ขาย (None / Pending / Approved / Rejected)

    [MaxLength(100)]
    public string? ShopName { get; set; } // ชื่อร้านค้าของผู้ขาย

    [MaxLength(30)]
    public string? Phone { get; set; } // เบอร์โทรศัพท์ติดต่อ

    [MaxLength(120)]
    public string? BankAccount { get; set; } // เลขบัญชีธนาคาร/พร้อมเพย์สำหรับรับเงินโอน

    [MaxLength(500)]
    public string? SellerBio { get; set; } // คำอธิบายแนะนำร้านค้าหรือประวัติผู้ขาย

    [MaxLength(300)]
    public string? SellerRejectReason { get; set; } // เหตุผลหากคำขอสมัครผู้ขายถูกปฏิเสธ

    public DateTime? SellerAppliedAt { get; set; } // วันเวลาที่ส่งคำขอสมัครเป็นผู้ขาย (UTC)

    // --- ความปลอดภัยบัญชี (Account Lockout & Password Reset) ---
    public int FailedLoginCount { get; set; } // จำนวนครั้งที่กรอกรหัสผ่านผิดติดต่อกัน (ใช้ป้องกัน Brute-force ใน AuthController)
    public DateTime? LockoutEndAt { get; set; } // เวลาสิ้นสุดการล็อกบัญชีชั่วคราวเมื่อกรอกรหัสผิดเกินกำหนด (UTC)
    [MaxLength(20)]
    public string? PasswordResetToken { get; set; } // โทเค็นชั่วคราวสำหรับรีเซ็ตรหัสผ่าน (POST /api/auth/forgot-password และ /reset-password)
    public DateTime? PasswordResetExpiresAt { get; set; } // วันเวลาหมดอายุของโทเค็นรีเซ็ตรหัสผ่าน (UTC)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่สร้างบัญชี (UTC)
}

// ตาราง Categories: เก็บหมวดหมู่สินค้าของเก่า (ใช้จัดกลุ่มสินค้าและคำนวณสิทธิ์ส่วนลดซื้อหมวดเดียวกัน)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ส่งออกไปแสดงตัวกรองหมวดหมู่ผ่าน GET /api/auctions และจัดการ CRUD ผ่าน /api/admin/categories
public class Category
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของหมวดหมู่

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty; // ชื่อหมวดหมู่ (ตั้ง Unique Index ใน AppDbContext)

    [MaxLength(10)]
    public string Symbol { get; set; } = "✦"; // สัญลักษณ์ไอคอนข้อความสั้น ๆ แสดงคู่กับชื่อหมวดหมู่บนหน้าเว็บ

    [MaxLength(200)]
    public string? Description { get; set; } // คำอธิบายรายละเอียดหมวดหมู่

    public bool IsActive { get; set; } = true; // เปิด/ปิดการใช้งานหมวดหมู่ (ถ้า false จะไม่แสดงให้เลือกตอนลงสินค้าใหม่)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่สร้างหมวดหมู่ (UTC)
}

// ตาราง AuctionItems: เก็บข้อมูลสินค้าประมูลแต่ละชิ้น ตั้งแต่ลงประกาศ เปิดประมูล จนปิดประมูลและได้ผู้ชนะ
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ส่งไปแสดงการ์ด/รายละเอียดสินค้าผ่าน GET /api/auctions และ GET /api/auctions/{id}
//    สร้าง/แก้ไขโดยผู้ขายผ่าน /api/seller/listings, เสนอราคาผ่าน POST /api/auctions/{id}/bid
public class AuctionItem
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของรายการประมูล

    // --- ข้อมูลพื้นฐานและผู้เกี่ยวข้อง ---
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty; // ชื่อสินค้าประมูล

    public int CategoryId { get; set; } // Foreign Key อ้างอิงหมวดหมู่สินค้า (Categories.Id)
    public Category? Category { get; set; } // Navigation Property ไปยังหมวดหมู่

    public int SellerId { get; set; } // Foreign Key อ้างอิงผู้ขายเจ้าของสินค้า (Users.Id)
    public User? Seller { get; set; } // Navigation Property ไปยังข้อมูลผู้ขาย

    // --- รายละเอียดสภาพสินค้าและรูปภาพ ---
    [Required, MaxLength(80)]
    public string Condition { get; set; } = string.Empty; // สภาพสินค้าและยุคสมัย เช่น "สภาพดี · ราวปี 1970"

    [Required, MaxLength(1500)]
    public string Description { get; set; } = string.Empty; // รายละเอียดและเรื่องราวของสินค้า

    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty; // URL รูปภาพสินค้า (รองรับทั้งไฟล์อัปโหลดใน wwwroot/uploads และลิงก์ภายนอก)

    // --- ข้อมูลราคาและช่วงเวลาประมูล ---
    public decimal StartPrice { get; set; } // ราคาเริ่มต้นประมูล (บาท)
    public decimal CurrentPrice { get; set; } // ราคาสูงสุดปัจจุบัน (อัปเดตทุกครั้งที่มีการบิดใหม่หรือโอนสิทธิ์ผู้ชนะอันดับถัดไป)
    public decimal MinIncrement { get; set; } = 50; // ขั้นต่ำในการเพิ่มราคาต่อครั้ง (บาท)

    public DateTime StartAt { get; set; } // วันเวลาเริ่มเปิดให้เสนอราคา (UTC)
    public DateTime EndAt { get; set; } // วันเวลาสิ้นสุดการประมูล (UTC — AuctionLifecycleService ตรวจสอบเพื่อปิดอัตโนมัติ)

    // --- สถานะการอนุมัติและผลผู้ชนะ ---
    [Required, MaxLength(20)]
    public string Status { get; set; } = AuctionStatuses.Pending; // สถานะสินค้า (Pending / Approved / Rejected / Closed)

    [MaxLength(400)]
    public string? RejectReason { get; set; } // เหตุผลหากแอดมินไม่อนุมัติสินค้า

    public int? WinnerId { get; set; } // Foreign Key อ้างอิงผู้ชนะประมูล (Users.Id — เป็น null หากยังไม่มีผู้ชนะหรือไม่มีคนบิด)
    public User? Winner { get; set; } // Navigation Property ไปยังข้อมูลผู้ชนะ

    // --- การควบคุมการแก้ไขพร้อมกัน (Optimistic Concurrency Control) ---
    [ConcurrencyCheck]
    public int Version { get; set; } = 1; // เลขเวอร์ชันใช้อ้างอิงตอนบันทึกป้องกัน Race Condition เมื่อมีผู้เสนอราคาพร้อมกันหลายคน

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่สร้างรายการประมูล (UTC)

    public List<Bid> Bids { get; set; } = []; // รายการประวัติการเสนอราคาทั้งหมดของสินค้าชิ้นนี้
}

// ตาราง Bids: เก็บประวัติการเสนอราคา (บิด) แต่ละครั้งของสินค้าประมูล
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] สร้างผ่าน POST /api/auctions/{id}/bid และดึงแสดงประวัติการบิดใน GET /api/auctions/{id}
public class Bid
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของการเสนอราคา

    public int AuctionItemId { get; set; } // Foreign Key อ้างอิงสินค้าที่เสนอราคา (AuctionItems.Id)
    public AuctionItem? AuctionItem { get; set; } // Navigation Property ไปยังรายการสินค้า

    public int BidderId { get; set; } // Foreign Key อ้างอิงผู้เสนอราคา (Users.Id)
    public User? Bidder { get; set; } // Navigation Property ไปยังผู้เสนอราคา

    public decimal Amount { get; set; } // จำนวนเงินที่เสนอราคา (บาท)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่เสนอราคา (UTC — ใช้ตัดสินกรณีราคาเท่ากันให้คนบิดก่อนชนะ)
}

// ตาราง SavedItems: เก็บรายการสินค้าที่ผู้ใช้กดบันทึก/ติดดาว (Watchlist)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] กดบันทึก/ยกเลิกผ่าน POST /api/auctions/{id}/save และแสดงผลใน GET /api/buyer/dashboard
public class SavedItem
{
    public int Id { get; set; } // รหัสหลัก (Primary Key)
    public int UserId { get; set; } // Foreign Key อ้างอิงผู้ใช้ที่กดบันทึก (Users.Id)
    public User? User { get; set; } // Navigation Property ไปยังผู้ใช้

    public int AuctionItemId { get; set; } // Foreign Key อ้างอิงสินค้าที่ถูกบันทึก (AuctionItems.Id)
    public AuctionItem? AuctionItem { get; set; } // Navigation Property ไปยังรายการสินค้า

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่กดบันทึก (UTC)
}

// ตาราง OrderPayments: เก็บคำสั่งซื้อ การคำนวณราคา/ส่วนลด/ค่าธรรมเนียม การชำระเงิน การนัดรับ และการปล่อยเงินให้ผู้ขาย (Escrow)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
//    - สร้างอัตโนมัติเมื่อปิดประมูลโดย AuctionLifecycleService.CloseExpiredAuctionsAsync()
//    - ผู้ซื้อดูรายการผ่าน GET /api/buyer/dashboard, ชำระเงินผ่าน POST /api/buyer/orders/{orderId}/pay, ยืนยันรับของผ่าน POST /api/buyer/orders/{orderId}/confirm-received
//    - ผู้ขายจัดการนัดรับ/ส่งมอบผ่าน GET /api/seller/dashboard และ POST /api/seller/orders/{orderId}/meetup, /deliver, /verify-payment
//    - ฝ่ายการเงินตรวจสลิป/ปล่อยเงิน/คืนเงินผ่าน GET /api/finance/dashboard และ POST /api/finance/orders/{orderId}/verify-payment, /release, /refund
public class OrderPayment
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของคำสั่งซื้อ

    [Required, MaxLength(30)]
    public string OrderCode { get; set; } = string.Empty; // รหัสคำสั่งซื้อที่แสดงให้ผู้ใช้ เช่น "AUC-00001" หรือ "AUC-00001-R2" (กรณีโอนสิทธิ์อันดับถัดไป)

    // --- ผู้เกี่ยวข้องและสินค้า ---
    public int AuctionItemId { get; set; } // Foreign Key อ้างอิงสินค้าที่ชนะประมูล (AuctionItems.Id)
    public AuctionItem? AuctionItem { get; set; } // Navigation Property ไปยังรายการสินค้า

    public int BuyerId { get; set; } // Foreign Key อ้างอิงผู้ซื้อที่ชนะประมูล (Users.Id)
    public User? Buyer { get; set; } // Navigation Property ไปยังผู้ซื้อ

    public int SellerId { get; set; } // Foreign Key อ้างอิงผู้ขายเจ้าของสินค้า (Users.Id)
    public User? Seller { get; set; } // Navigation Property ไปยังผู้ขาย

    // --- โครงสร้างราคา ส่วนลดซื้อหมวดเดียวกัน และค่าธรรมเนียมแพลตฟอร์ม ---
    public decimal WinningPrice { get; set; } // ราคาที่ชนะการประมูลก่อนหักส่วนลด (บาท)
    public decimal DiscountPercent { get; set; } // เปอร์เซ็นต์ส่วนลดที่ได้รับ (เช่น 10% เมื่อเคยซื้อและชำระแล้วในหมวดเดียวกัน)
    [MaxLength(160)]
    public string? DiscountReason { get; set; } // ข้อความอธิบายเหตุผลการได้ส่วนลดเพื่อแสดงบนหน้าชำระเงิน
    public decimal DiscountAmount { get; set; } // จำนวนเงินส่วนลด (บาท)
    public decimal FinalBuyerAmount { get; set; } // ยอดสุทธิที่ผู้ซื้อต้องชำระจริง = WinningPrice - DiscountAmount

    public decimal PlatformFeePercent { get; set; } // เปอร์เซ็นต์ค่าธรรมเนียมแพลตฟอร์ม (อ้างอิงจาก FinanceSettings)
    public decimal PlatformFeeAmount { get; set; } // จำนวนเงินค่าธรรมเนียมแพลตฟอร์ม (บาท)
    public decimal SellerNetAmount { get; set; } // ยอดเงินสุทธิที่ผู้ขายจะได้รับ = FinalBuyerAmount - PlatformFeeAmount

    // --- ข้อมูลการชำระเงินและหลักฐานสลิป ---
    [Required, MaxLength(30)]
    public string PaymentMethod { get; set; } = PaymentMethods.PlatformEscrow; // วิธีชำระเงิน (PlatformEscrow หรือ DirectToSeller)

    [Required, MaxLength(30)]
    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid; // สถานะการชำระเงิน (Unpaid / PendingVerification / Paid / Refunded / Cancelled)
    [MaxLength(400)]
    public string? PaymentSlipUrl { get; set; } // พาธ URL รูปภาพสลิปโอนเงินที่ผู้ซื้ออัปโหลด (เก็บใน wwwroot/uploads/slips)
    [MaxLength(300)]
    public string? PaymentNote { get; set; } // หมายเหตุการโอนเงินจากผู้ซื้อ
    public DateTime? PaymentSubmittedAt { get; set; } // วันเวลาที่ผู้ซื้อส่งหลักฐานการชำระเงิน (UTC)
    [MaxLength(300)]
    public string? PaymentRejectReason { get; set; } // เหตุผลหากสลิปถูกปฏิเสธและให้ส่งใหม่
    public DateTime? PaymentDueAt { get; set; } // เส้นตายการชำระเงิน (UTC — ปกติ +48 ชม. หากเลยกำหนด AuctionLifecycleService จะยกเลิกและส่งสิทธิ์ต่อ)
    public DateTime? PaidAt { get; set; } // วันเวลาที่ยืนยันชำระเงินสำเร็จ (UTC)

    // --- ข้อมูลการนัดรับและส่งมอบสินค้า ---
    [Required, MaxLength(20)]
    public string MeetupStatus { get; set; } = MeetupStatuses.NotScheduled; // สถานะการนัดรับ (NotScheduled / Scheduled / Delivered / Completed)
    [MaxLength(200)]
    public string? MeetupLocation { get; set; } // สถานที่นัดรับสินค้า เช่น "BTS อารีย์ ทางออก 3"
    public DateTime? MeetupAt { get; set; } // วันเวลานัดรับสินค้า (UTC)
    [MaxLength(300)]
    public string? MeetupNote { get; set; } // หมายเหตุเพิ่มเติมสำหรับการนัดรับ
    public DateTime? DeliveredAt { get; set; } // วันเวลาที่ผู้ขายกดยืนยันส่งมอบสินค้า (UTC — ใช้คำนวณครบ 3 วันเพื่อปลดล็อกปล่อยเงิน Escrow อัตโนมัติ)
    public DateTime? ReceivedAt { get; set; } // วันเวลาที่ผู้ซื้อกดยืนยันได้รับสินค้า (UTC)

    // --- ข้อมูลการปล่อยเงิน/คืนเงิน (Payout & Finance) ---
    [Required, MaxLength(20)]
    public string PayoutStatus { get; set; } = PayoutStatuses.Pending; // สถานะการโอนเงินให้ผู้ขาย (Pending / Released / DirectSettled / Refunded / Cancelled)
    public DateTime? PayoutReleasedAt { get; set; } // วันเวลาที่ฝ่ายการเงินปล่อยเงินให้ผู้ขาย (UTC)
    public DateTime? RefundedAt { get; set; } // วันเวลาที่ฝ่ายการเงินคืนเงินให้ผู้ซื้อ (UTC)
    [MaxLength(300)]
    public string? FinanceNote { get; set; } // บันทึกหมายเหตุจากฝ่ายการเงินหรือระบบอัตโนมัติ

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่สร้างคำสั่งซื้อ (UTC)
}

// ตาราง Conversations: เก็บห้องแชทระหว่างผู้ซื้อกับผู้ขายสำหรับสินค้าประมูลแต่ละชิ้น (1 คู่ ผู้ซื้อ-ผู้ขาย ต่อ 1 สินค้า)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ใช้งานผ่าน ChatController (GET /api/chat/conversations, POST /api/chat/start)
//    และสร้างอัตโนมัติเมื่อชนะประมูลโดย AuctionLifecycleService.EnsureWinnerConversationAsync()
public class Conversation
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของห้องแชท

    public int AuctionItemId { get; set; } // Foreign Key อ้างอิงสินค้าที่คุยกัน (AuctionItems.Id)
    public AuctionItem? AuctionItem { get; set; } // Navigation Property ไปยังรายการสินค้า

    public int BuyerId { get; set; } // Foreign Key อ้างอิงผู้ซื้อ (Users.Id)
    public User? Buyer { get; set; } // Navigation Property ไปยังผู้ซื้อ

    public int SellerId { get; set; } // Foreign Key อ้างอิงผู้ขาย (Users.Id)
    public User? Seller { get; set; } // Navigation Property ไปยังผู้ขาย

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่เปิดห้องแชท (UTC)
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่มีข้อความล่าสุด (ใช้เรียงลำดับห้องแชทบนหน้าบ้าน)
    public DateTime? BuyerLastReadAt { get; set; } // เวลาล่าสุดที่ผู้ซื้อเปิดอ่านแชท (ใช้คำนวณจำนวนข้อความยังไม่อ่านใน /api/notifications/summary)
    public DateTime? SellerLastReadAt { get; set; } // เวลาล่าสุดที่ผู้ขายเปิดอ่านแชท (ใช้คำนวณจำนวนข้อความยังไม่อ่านใน /api/notifications/summary)

    public List<ChatMessage> Messages { get; set; } = []; // รายการข้อความทั้งหมดในห้องแชทนี้
    public List<Dispute> Disputes { get; set; } = []; // รายการข้อพิพาทที่ถูกเปิดจากห้องแชทนี้ (แอดมินดูข้อความได้เฉพาะห้องที่มีข้อพิพาท)
}

// ตาราง Notifications: เก็บการแจ้งเตือนแบบ In-App สำหรับผู้ใช้แต่ละคน (เช่น ชนะประมูล, ข้อความใหม่, ข้อพิพาท, หมดเวลาชำระเงิน)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] หน้าบ้าน (wwwroot/js/site.js) ทำ Polling ดึงข้อมูลเป็นระยะผ่าน
//    GET /api/notifications/summary และทำเครื่องหมายอ่านแล้วผ่าน POST /api/notifications/{id}/read หรือ /read-all
public class Notification
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของการแจ้งเตือน

    public int UserId { get; set; } // Foreign Key อ้างอิงผู้รับการแจ้งเตือน (Users.Id)
    public User? User { get; set; } // Navigation Property ไปยังผู้รับ

    [Required, MaxLength(40)]
    public string Type { get; set; } = "info"; // ประเภทการแจ้งเตือน เช่น "auction_won", "payment", "message", "dispute", "payment_expired"

    [Required, MaxLength(140)]
    public string Title { get; set; } = string.Empty; // หัวข้อการแจ้งเตือนที่แสดงในกระดิ่งแจ้งเตือน

    [Required, MaxLength(500)]
    public string Body { get; set; } = string.Empty; // เนื้อหารายละเอียดการแจ้งเตือน

    [MaxLength(40)]
    public string? TargetView { get; set; } // ชื่อหน้าจอปลายทางบนหน้าบ้านเมื่อผู้ใช้คลิกแจ้งเตือน เช่น "payments", "messages", "seller-orders", "admin-disputes"

    public int? ReferenceId { get; set; } // รหัสอ้างอิงของรายการที่เกี่ยวข้อง (เช่น OrderId, ConversationId, AuctionItemId) เพื่อให้หน้าบ้านเปิดรายการนั้นตรงจุด
    public bool IsRead { get; set; } // สถานะว่าผู้ใช้อ่านแล้วหรือยัง (false = นับรวมในตัวเลขจุดแดง Unread Badge)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่สร้างการแจ้งเตือน (UTC)
}

// ตาราง ChatMessages: เก็บข้อความแชทแต่ละข้อความภายในห้องสนทนา (Conversation)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ดึง/ส่งข้อความผ่าน GET/POST /api/chat/conversations/{id}/messages
//    และแอดมินตรวจสอบหลักฐานกรณีมีข้อพิพาทผ่าน GET /api/admin/disputes/{disputeId}/messages
public class ChatMessage
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของข้อความ

    public int ConversationId { get; set; } // Foreign Key อ้างอิงห้องสนทนา (Conversations.Id)
    public Conversation? Conversation { get; set; } // Navigation Property ไปยังห้องสนทนา

    public int SenderId { get; set; } // Foreign Key อ้างอิงผู้ส่งข้อความ (Users.Id)
    public User? Sender { get; set; } // Navigation Property ไปยังผู้ส่ง

    [Required, MaxLength(1500)]
    public string Body { get; set; } = string.Empty; // เนื้อหาข้อความที่ส่ง

    public DateTime SentAt { get; set; } = DateTime.UtcNow; // วันเวลาที่ส่งข้อความ (UTC)
}

// ตาราง Disputes: เก็บคำร้องเรียน/ข้อพิพาทระหว่างผู้ซื้อกับผู้ขาย เพื่อให้แอดมินเข้ามาไกล่เกลี่ยและระงับการปล่อยเงิน Escrow ชั่วคราว
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ผู้ใช้เปิดข้อพิพาทผ่าน POST /api/chat/conversations/{id}/dispute
//    และแอดมินดู/อัปเดตสถานะผ่าน GET /api/admin/dashboard, GET /api/admin/disputes/{id}/messages, POST /api/admin/disputes/{id}/update
public class Dispute
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของข้อพิพาท

    public int ConversationId { get; set; } // Foreign Key อ้างอิงห้องแชทที่เกิดข้อพิพาท (Conversations.Id)
    public Conversation? Conversation { get; set; } // Navigation Property ไปยังห้องแชท

    public int? OrderPaymentId { get; set; } // Foreign Key อ้างอิงคำสั่งซื้อที่เกี่ยวข้อง (OrderPayments.Id — ใช้เช็กบล็อกการปล่อยเงินใน CanReleaseEscrow)
    public OrderPayment? OrderPayment { get; set; } // Navigation Property ไปยังคำสั่งซื้อ

    public int ReporterId { get; set; } // Foreign Key อ้างอิงผู้แจ้งข้อพิพาท (Users.Id)
    public User? Reporter { get; set; } // Navigation Property ไปยังผู้แจ้ง

    [Required, MaxLength(120)]
    public string Reason { get; set; } = string.Empty; // หัวข้อสาเหตุข้อพิพาท เช่น "สินค้าไม่ตรงตามรายละเอียด"

    [Required, MaxLength(1500)]
    public string Details { get; set; } = string.Empty; // คำอธิบายรายละเอียดปัญหาจากผู้แจ้ง

    [Required, MaxLength(20)]
    public string Status { get; set; } = DisputeStatuses.Open; // สถานะข้อพิพาท (Open / Investigating / Resolved)

    [MaxLength(500)]
    public string? AdminNote { get; set; } // บันทึกผลการตรวจสอบหรือแนวทางแก้ไขจากแอดมิน

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่เปิดข้อพิพาท (UTC)
    public DateTime? ResolvedAt { get; set; } // วันเวลาที่ปิดข้อพิพาทสำเร็จ (UTC)
}

// ตาราง FinanceSettings: เก็บการตั้งค่าระบบการเงินกลางแบบแถวเดียว (Singleton Row: Id = 1)
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ส่งค่าไปแสดงหน้าเว็บผ่าน GET /api/auth/me, GET /api/auctions, GET /api/finance/dashboard
//    และฝ่ายการเงินแก้ไขผ่าน PUT /api/finance/settings (เมื่อแก้จะคำนวณคำสั่งซื้อที่ยังไม่ชำระใหม่ทันที)
public class FinanceSettings
{
    public int Id { get; set; } = 1; // รหัสหลักคงที่เป็น 1 (เก็บการตั้งค่ากลางของแพลตฟอร์มเพียง 1 เรคคอร์ด)
    public decimal SameCategoryDiscountPercent { get; set; } = 10; // เปอร์เซ็นต์ส่วนลดเมื่อซื้อสินค้าซ้ำในหมวดหมู่เดียวกัน (ค่าเริ่มต้น 10%)
    public decimal PlatformFeePercent { get; set; } = 5; // เปอร์เซ็นต์ค่าธรรมเนียมแพลตฟอร์มที่หักจากยอดขาย (ค่าเริ่มต้น 5%)
    public decimal GlobalMinBidIncrement { get; set; } = 50; // ขั้นต่ำการเพิ่มราคาบิดกลางของระบบ (บาท)
    [MaxLength(40)]
    public string PlatformPromptPayId { get; set; } = "081-999-8888"; // เลขพร้อมเพย์บัญชีกลาง Escrow สำหรับให้ผู้ซื้อโอนชำระเงิน
    [MaxLength(120)]
    public string PlatformAccountName { get; set; } = "บจก. ของเก่าเล่าเรื่อง (บัญชีกลาง)"; // ชื่อบัญชีกลาง Escrow ที่แสดงบนหน้าชำระเงิน
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่อัปเดตการตั้งค่าล่าสุด (UTC)
}

// ตาราง AuditLogs: เก็บประวัติการกระทำสำคัญของแอดมินและฝ่ายการเงิน (Audit Trail) เพื่อความโปร่งใสและตรวจสอบย้อนหลัง
// 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] บันทึกจาก AdminController/FinanceController และแสดงในหน้าประวัติตรวจสอบผ่าน GET /api/audit-logs (wwwroot/js/site.js)
public class AuditLog
{
    public int Id { get; set; } // รหัสหลัก (Primary Key) ของบันทึกประวัติ

    public int ActorId { get; set; } // Foreign Key อ้างอิงผู้ดำเนินการ (Users.Id)
    public User? Actor { get; set; } // Navigation Property ไปยังผู้ดำเนินการ

    [Required, MaxLength(80)]
    public string ActorName { get; set; } = string.Empty; // ชื่อผู้ดำเนินการ ณ เวลาที่ทำรายการ (เก็บ Snapshot เผื่อมีการเปลี่ยนชื่อภายหลัง)

    [Required, MaxLength(20)]
    public string ActorRole { get; set; } = string.Empty; // บทบาทของผู้ดำเนินการ ณ เวลาที่ทำรายการ (เช่น Admin / Finance)

    [Required, MaxLength(60)]
    public string Action { get; set; } = string.Empty; // ชื่อการกระทำ เช่น "ApproveAuction", "ReleasePayout", "SuspendUser"

    [Required, MaxLength(40)]
    public string TargetType { get; set; } = string.Empty; // ประเภทของข้อมูลเป้าหมาย เช่น "AuctionItem", "OrderPayment", "User"

    public int? TargetId { get; set; } // รหัสของข้อมูลเป้าหมายที่ถูกดำเนินการ

    [Required, MaxLength(600)]
    public string Details { get; set; } = string.Empty; // รายละเอียดการเปลี่ยนแปลงหรือเหตุผลประกอบ

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // วันเวลาที่เกิดเหตุการณ์ (UTC — มี Index เพื่อค้นหาเรียงตามเวลาได้เร็ว)
}
