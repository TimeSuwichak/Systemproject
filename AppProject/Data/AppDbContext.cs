// ============================================================================
// 📄 ชื่อไฟล์: AppProject/Data/AppDbContext.cs
// 🏷️ บทบาท: ฝั่งหลังบ้าน (Backend) — ชั้นเชื่อมต่อฐานข้อมูล (Entity Framework Core DbContext)
// 📝 หน้าที่โดยรวม:
//    กำหนดตารางฐานข้อมูล (DbSet) ทั้ง 12 ตาราง, ตัวแปลงเวลาเป็น UTC อัตโนมัติสำหรับ SQLite,
//    ดัชนี (Indexes / Unique Constraints) และความสัมพันธ์ระหว่างตาราง (Foreign Keys & DeleteBehavior)
// 🔄 การเชื่อมต่อกับส่วนอื่นในระบบ:
//    - ลงทะเบียนใช้งานผ่าน Dependency Injection ใน AppProject/Program.cs (ใช้ SQLite ไฟล์ auction.db)
//    - อ้างอิงโครงสร้าง Entity จาก AppProject/Models/Entities.cs
//    - ถูกฉีด (Inject) เข้าสู่ AppProject/Data/DbSeeder.cs, AppProject/Services/AuctionLifecycleService.cs
//      และทุก API Controller (ผ่าน ApiBaseController) เพื่ออ่าน/เขียนข้อมูลให้ฝั่งหน้าบ้าน (wwwroot/js/site.js)
// ============================================================================

using AppProject.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppProject.Data;

// คลาส AppDbContext สืบทอดจาก EF Core DbContext สำหรับจัดการการเข้าถึงฐานข้อมูล SQLite ทั้งหมดของระบบ
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ------------------------------------------------------------------------
    // ตัวแปลงค่า DateTime สำหรับ SQLite (Value Converters)
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
    //    SQLite เก็บค่า DateTime เป็นข้อความ (TEXT) ทำให้ตอนอ่านกลับค่า DateTime.Kind จะกลายเป็น Unspecified
    //    ตัวแปลงนี้บังคับเก็บและอ่านค่ากลับเป็น DateTimeKind.Utc เสมอ เพื่อให้ ASP.NET Core แปลง JSON เป็นรูปแบบ ISO-8601 ที่ลงท้ายด้วย "Z"
    //    ส่งผลให้ฝั่งหน้าบ้าน (wwwroot/js/site.js) ใช้ new Date(...) คำนวณเวลานับถอยหลังปิดประมูล (EndAt)
    //    และเวลาครบกำหนดชำระเงิน (PaymentDueAt) ตามเขตเวลาของเบราว์เซอร์ได้อย่างแม่นยำไม่เพี้ยน 7 ชั่วโมง
    // ------------------------------------------------------------------------

    // ตัวแปลงสำหรับฟิลด์ DateTime (ไม่เป็น null): ขาเขียนแปลงเป็น UTC, ขาอ่านระบุ Kind = Utc
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    // ตัวแปลงสำหรับฟิลด์ DateTime? (อนุญาตให้เป็น null): แปลงเป็น UTC เฉพาะเมื่อมีค่า
    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    // ------------------------------------------------------------------------
    // รายการตารางฐานข้อมูล (DbSet Properties — ทั้งหมด 12 ตาราง)
    // ------------------------------------------------------------------------
    public DbSet<User> Users => Set<User>();                            // ตารางบัญชีผู้ใช้งาน (ผู้ซื้อ ผู้ขาย แอดมิน การเงิน)
    public DbSet<Category> Categories => Set<Category>();               // ตารางหมวดหมู่สินค้าของเก่า
    public DbSet<AuctionItem> AuctionItems => Set<AuctionItem>();       // ตารางรายการสินค้าประมูล
    public DbSet<Bid> Bids => Set<Bid>();                               // ตารางประวัติการเสนอราคาประมูล
    public DbSet<SavedItem> SavedItems => Set<SavedItem>();             // ตารางสินค้าที่ผู้ใช้กดบันทึกไว้ดูภายหลัง
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();    // ตารางคำสั่งซื้อ การชำระเงิน นัดรับ และการปล่อยเงิน Escrow
    public DbSet<Conversation> Conversations => Set<Conversation>();    // ตารางห้องแชทระหว่างผู้ซื้อ-ผู้ขาย
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();       // ตารางข้อความแชทภายในห้องสนทนา
    public DbSet<Dispute> Disputes => Set<Dispute>();                   // ตารางคำร้องเรียน/ข้อพิพาท
    public DbSet<FinanceSettings> FinanceSettings => Set<FinanceSettings>(); // ตารางตั้งค่าการเงินกลาง (ส่วนลด ค่าธรรมเนียม พร้อมเพย์กลาง)
    public DbSet<Notification> Notifications => Set<Notification>();    // ตารางการแจ้งเตือนในแอป (หน้าบ้าน Polling ผ่าน GET /api/notifications/summary)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();                // ตารางบันทึกประวัติการดำเนินการของแอดมิน/ฝ่ายการเงิน

    // กำหนดกติกาแปลงค่ามาตรฐานระดับทั้งฐานข้อมูล (Model-wide Conventions)
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // บังคับใช้ตัวแปลง UTC กับทุก Property ที่เป็นชนิด DateTime และ DateTime? ในทุกตารางโดยอัตโนมัติ
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    // กำหนดโครงสร้างดัชนี (Indexes) ความสัมพันธ์ระหว่างตาราง (Relationships) และพฤติกรรมการลบ (DeleteBehavior)
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ตาราง Users: ห้ามใช้อีเมลซ้ำกันทั้งระบบ (Unique Index)
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // ตาราง Categories: ห้ามตั้งชื่อหมวดหมู่ซ้ำกัน (Unique Index)
        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        // ตาราง AuctionItems: ความสัมพันธ์กับ Category, Seller และ Winner
        modelBuilder.Entity<AuctionItem>(entity =>
        {
            // สินค้า 1 ชิ้นอยู่ใน 1 หมวดหมู่ — ใช้ Restrict เพื่อห้ามลบหมวดหมู่หากยังมีสินค้าผูกอยู่
            entity.HasOne(a => a.Category)
                .WithMany()
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // สินค้า 1 ชิ้นมีผู้ขาย 1 คน — ใช้ Restrict เพื่อป้องกันการลบบัญชีผู้ขายที่มีประวัติสินค้า
            entity.HasOne(a => a.Seller)
                .WithMany()
                .HasForeignKey(a => a.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // สินค้า 1 ชิ้นมีผู้ชนะได้ 1 คน (หรือ null) — ใช้ SetNull เผื่อกรณีลบบัญชีผู้ชนะให้ค่า WinnerId เป็น null แทนการลบสินค้า
            entity.HasOne(a => a.Winner)
                .WithMany()
                .HasForeignKey(a => a.WinnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ตาราง Bids: ความสัมพันธ์กับ AuctionItem และ Bidder
        modelBuilder.Entity<Bid>(entity =>
        {
            // ผูกกับรายการสินค้าประมูล (1 สินค้ามีหลาย Bid) — ใช้ Cascade หากลบสินค้าให้ลบประวัติการบิดของสินค้านั้นตามไปด้วย
            entity.HasOne(b => b.AuctionItem)
                .WithMany(a => a.Bids)
                .HasForeignKey(b => b.AuctionItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // ผูกกับผู้ใช้ที่เสนอราคา — ใช้ Restrict เพื่อรักษาประวัติการเสนอราคาไม่ให้สูญหาย
            entity.HasOne(b => b.Bidder)
                .WithMany()
                .HasForeignKey(b => b.BidderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ตาราง SavedItems: สินค้าที่ผู้ใช้กดบันทึก (Watchlist)
        modelBuilder.Entity<SavedItem>(entity =>
        {
            // Composite Unique Index: ป้องกันไม่ให้ผู้ใช้ 1 คนบันทึกสินค้าชิ้นเดียวกันซ้ำหลายแถว
            entity.HasIndex(s => new { s.UserId, s.AuctionItemId }).IsUnique();
            // หากลบผู้ใช้หรือลบสินค้า ให้ลบรายการบันทึกที่เกี่ยวข้องทิ้งอัตโนมัติ (Cascade)
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.AuctionItem).WithMany().HasForeignKey(s => s.AuctionItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ตาราง OrderPayments: คำสั่งซื้อและการชำระเงิน
        modelBuilder.Entity<OrderPayment>(entity =>
        {
            // Composite Unique Index (AuctionItemId, BuyerId): ป้องกันการสร้างออเดอร์ซ้ำให้ผู้ซื้อคนเดิมในสินค้าเดียวกัน
            // แต่ยังอนุญาตให้สินค้าชิ้นเดิมสร้างออเดอร์ใหม่ให้ "ผู้ซื้ออันดับถัดไป (Runner-up)" ได้ หากผู้ชนะคนแรกไม่ชำระเงินใน 48 ชม.
            entity.HasIndex(o => new { o.AuctionItemId, o.BuyerId }).IsUnique();
            // รหัสคำสั่งซื้อ (OrderCode เช่น AUC-00001) ต้องไม่ซ้ำกันทั้งระบบ
            entity.HasIndex(o => o.OrderCode).IsUnique();
            // ใช้ Restrict ทั้ง 3 ความสัมพันธ์ เพื่อห้ามลบสินค้า ผู้ซื้อ หรือผู้ขาย ที่มีประวัติธุรกรรมการเงินอยู่แล้ว
            entity.HasOne(o => o.AuctionItem).WithMany().HasForeignKey(o => o.AuctionItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.Seller).WithMany().HasForeignKey(o => o.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        // ตาราง Conversations: ห้องแชทระหว่างผู้ซื้อ-ผู้ขาย
        modelBuilder.Entity<Conversation>(entity =>
        {
            // Composite Unique Index: 1 สินค้า + 1 ผู้ซื้อ + 1 ผู้ขาย มีได้เพียง 1 ห้องแชทเท่านั้น
            entity.HasIndex(c => new { c.AuctionItemId, c.BuyerId, c.SellerId }).IsUnique();
            // หากลบสินค้าให้ลบห้องแชทตาม (Cascade) แต่ห้ามลบผู้ซื้อหรือผู้ขายที่ยังมีห้องแชทอยู่ (Restrict)
            entity.HasOne(c => c.AuctionItem).WithMany().HasForeignKey(c => c.AuctionItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.Buyer).WithMany().HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.Seller).WithMany().HasForeignKey(c => c.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        // ตาราง ChatMessages: ข้อความในห้องแชท
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            // หากลบห้องสนทนาให้ลบข้อความทั้งหมดในห้องนั้นตาม (Cascade)
            entity.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            // ห้ามลบบัญชีผู้ส่งข้อความเพื่อรักษาประวัติการสนทนา (Restrict)
            entity.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
        });

        // ตาราง Disputes: ข้อพิพาท/คำร้องเรียน
        modelBuilder.Entity<Dispute>(entity =>
        {
            // หากลบห้องแชทให้ลบข้อพิพาทของห้องนั้นตาม (Cascade)
            entity.HasOne(d => d.Conversation).WithMany(c => c.Disputes).HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.Cascade);
            // ผูกกับคำสั่งซื้อแบบไม่บังคับ (Nullable) — ใช้ SetNull หากลบคำสั่งซื้อ
            entity.HasOne(d => d.OrderPayment).WithMany().HasForeignKey(d => d.OrderPaymentId).OnDelete(DeleteBehavior.SetNull);
            // ห้ามลบบัญชีผู้แจ้งข้อพิพาท (Restrict)
            entity.HasOne(d => d.Reporter).WithMany().HasForeignKey(d => d.ReporterId).OnDelete(DeleteBehavior.Restrict);
        });

        // ตาราง Notifications: การแจ้งเตือนผู้ใช้
        modelBuilder.Entity<Notification>(entity =>
        {
            // Composite Index (UserId, IsRead, CreatedAt): เร่งความเร็ว Query ที่หน้าบ้านยิง Polling บ่อย ๆ ผ่าน GET /api/notifications/summary
            entity.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
            // หากลบผู้ใช้ให้ลบการแจ้งเตือนของผู้ใช้นั้นทั้งหมดตาม (Cascade)
            entity.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ตาราง AuditLogs: บันทึกประวัติการตรวจสอบ (Audit Trail)
        modelBuilder.Entity<AuditLog>(entity =>
        {
            // Index บน CreatedAt เพื่อเรียงลำดับประวัติล่าสุดใน GET /api/audit-logs ได้รวดเร็ว
            entity.HasIndex(a => a.CreatedAt);
            // ใช้ Restrict เพื่อรักษาประวัติการกระทำของแอดมินและฝ่ายการเงินไว้ตรวจสอบย้อนหลังเสมอ
            entity.HasOne(a => a.Actor).WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
