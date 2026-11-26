// ============================================================================
// 📄 ชื่อไฟล์: AppProject/Data/DbSeeder.cs
// 🏷️ บทบาท: ฝั่งหลังบ้าน (Backend) — ตัวอัปเกรดโครงสร้างฐานข้อมูลและเติมข้อมูลตัวอย่างเริ่มต้น (Database Migration & Seeder)
// 📝 หน้าที่โดยรวม:
//    1) ตรวจสอบและอัปเกรดฐานข้อมูล SQLite รุ่นเก่า (ที่สร้างก่อนใช้ EF Migrations) และรัน Database.MigrateAsync()
//    2) เติมข้อมูลตัวอย่างเริ่มต้น (Seed Data) เมื่อฐานข้อมูลยังว่างเปล่า เช่น บัญชีทดลองใช้งาน, หมวดหมู่,
//       สินค้าประมูล (ทั้งเปิดประมูล/รออนุมัติ/ปิดแล้ว), ประวัติบิด, คำสั่งซื้อ, แชท, ข้อพิพาท และการแจ้งเตือน
// 🔄 การเชื่อมต่อกับส่วนอื่นในระบบ:
//    - ถูกเรียกทำงานอัตโนมัติทุกครั้งเมื่อเปิดเซิร์ฟเวอร์ใน AppProject/Program.cs (DbSeeder.SeedAsync(db))
//    - ใช้ AppDbContext (AppProject/Data/AppDbContext.cs), Entities (AppProject/Models/Entities.cs) และ PasswordService
//    - บัญชีตัวอย่างและข้อมูลที่เติมในไฟล์นี้จะถูกส่งไปยังฝั่งหน้าบ้าน (wwwroot/js/site.js) ผ่าน API ต่าง ๆ ทันทีที่เปิดเว็บ
// ============================================================================

using AppProject.Models;
using AppProject.Services;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Data;

// คลาสสแตติกสำหรับอัปเกรด Schema และสร้างข้อมูลจำลองเริ่มต้นของระบบประมูล "ของเก่าเล่าเรื่อง"
public static class DbSeeder
{
    // เมธอดหลักสำหรับเตรียมฐานข้อมูลและเติมข้อมูลเริ่มต้น (เรียกครั้งเดียวตอน Startup ใน Program.cs)
    public static async Task SeedAsync(AppDbContext db)
    {
        // ขั้นตอนที่ 1: อัปเกรดฐานข้อมูลรุ่นเก่า (ถ้ามี) และรัน EF Core Migrations ให้ตารางครบถ้วนก่อนเสมอ
        await MigrateOrUpgradeLegacyDbAsync(db);

        // ป้องกันการเติมข้อมูลซ้ำ (Idempotent): หากมีผู้ใช้อยู่ในระบบแล้วให้จบการทำงานทันที
        if (await db.Users.AnyAsync())
            return;

        // ใช้เวลาปัจจุบันแบบ UTC เป็นฐานอ้างอิงในการกำหนดเวลาเริ่มต้น/สิ้นสุดของข้อมูลตัวอย่างทั้งหมด
        var now = DateTime.UtcNow;

        // --------------------------------------------------------------------
        // 1. ตั้งค่าการเงินกลางของแพลตฟอร์ม (FinanceSettings — แถวเดียว Id = 1)
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ค่าเหล่านี้ส่งให้หน้าบ้านผ่าน GET /api/auth/me และ GET /api/finance/dashboard
        // --------------------------------------------------------------------
        var settings = new FinanceSettings
        {
            Id = 1,
            SameCategoryDiscountPercent = 10, // ส่วนลดซื้อสินค้าหมวดเดียวกัน 10%
            PlatformFeePercent = 5,           // ค่าธรรมเนียมแพลตฟอร์ม 5%
            GlobalMinBidIncrement = 50,       // ขั้นต่ำในการเพิ่มราคาบิดกลาง 50 บาท
            UpdatedAt = now
        };
        db.FinanceSettings.Add(settings);

        // --------------------------------------------------------------------
        // 2. สร้างบัญชีผู้ใช้ตัวอย่างครบทุกบทบาท (รหัสผ่านทุกบัญชีคือ "123456")
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] หน้าบ้าน (wwwroot/js/site.js) ดึงรายชื่อบัญชีทดลองผ่าน GET /api/auth/demo-accounts
        //    และล็อกอินผ่าน POST /api/auth/login (AuthController) เพื่อสร้าง Auth Cookie
        // --------------------------------------------------------------------

        // ผู้ซื้อคนที่ 1 (มีประวัติชนะประมูลและได้สิทธิ์ส่วนลดหมวดเดียวกัน 10%)
        var buyer1 = new User
        {
            DisplayName = "คุณพิมพ์ชนก (ผู้ประมูล)",
            Email = "buyer@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Buyer,
            SellerStatus = SellerStatuses.None,
            Phone = "081-234-5678",
            CreatedAt = now.AddDays(-20)
        };

        // ผู้ซื้อคนที่ 2 (ส่งคำขอสมัครเป็นผู้ขายไว้ สถานะ Pending เพื่อให้แอดมินทดลองกดอนุมัติ)
        var buyer2 = new User
        {
            DisplayName = "คุณปกรณ์ นักสะสม",
            Email = "pakorn@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Buyer,
            SellerStatus = SellerStatuses.Pending,
            ShopName = "กรุของเก่าบ้านปกรณ์",
            Phone = "082-345-6789",
            BankAccount = "กสิกรไทย 012-3-45678-9 (นายปกรณ์ วัฒนา)",
            SellerBio = "สะสมเครื่องครัวโบราณและของใช้ญี่ปุ่นมากว่า 10 ปี มีของแท้พร้อมใบรับรอง",
            SellerAppliedAt = now.AddHours(-5),
            CreatedAt = now.AddDays(-18)
        };

        // ผู้ขายคนที่ 1 (ได้รับการอนุมัติแล้ว — เจ้าของสินค้ามีดญี่ปุ่น, เหรียญ ร.5, นาฬิกาไขลาน)
        var seller1 = new User
        {
            DisplayName = "คุณชาญชัย (ผู้ขาย)",
            Email = "seller@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Seller,
            SellerStatus = SellerStatuses.Approved,
            ShopName = "เรือนสะสมชาญชัย",
            Phone = "089-876-5432",
            BankAccount = "ไทยพาณิชย์ 405-1-23456-7 (นายชาญชัย สมบูรณ์)",
            SellerBio = "คัดสรรมีดครัวญี่ปุ่น เซรามิก และของสะสมหายาก นัดรับได้ย่านจตุจักรและอารีย์",
            SellerAppliedAt = now.AddDays(-30),
            CreatedAt = now.AddDays(-30)
        };

        // ผู้ขายคนที่ 2 (ได้รับการอนุมัติแล้ว — เจ้าของสินค้าชุดชงชา, แจกัน, กาน้ำชา และถ้วยชา)
        var seller2 = new User
        {
            DisplayName = "คุณรัตนา (ผู้ขาย)",
            Email = "rattana@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Seller,
            SellerStatus = SellerStatuses.Approved,
            ShopName = "บ้านชาและครามรัตนา",
            Phone = "086-111-2233",
            BankAccount = "กรุงเทพ 123-4-56789-0 (นางสาวรัตนา ศรีสุข)",
            SellerBio = "จำหน่ายชุดชงชาโบราณและงานเซรามิกญี่ปุ่นแท้",
            SellerAppliedAt = now.AddDays(-25),
            CreatedAt = now.AddDays(-25)
        };

        // บัญชีผู้ขายที่รออนุมัติ (แสดงในคิวอนุมัติผู้ขายของหน้า Admin Dashboard)
        var pendingSeller = new User
        {
            DisplayName = "คุณวิชัย (รออนุมัติผู้ขาย)",
            Email = "pending-seller@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Seller,
            SellerStatus = SellerStatuses.Pending,
            ShopName = "คลังเหล็กหล่อวินเทจ",
            Phone = "085-999-8877",
            BankAccount = "กรุงไทย 987-6-54321-0 (นายวิชัย กล้าหาญ)",
            SellerBio = "สะสมกระทะเหล็กหล่อและเครื่องครัวยุค 1960–1980",
            SellerAppliedAt = now.AddHours(-3),
            CreatedAt = now.AddDays(-2)
        };

        // บัญชีผู้ดูแลระบบ (Admin) สำหรับจัดการอนุมัติผู้ขาย สินค้า หมวดหมู่ และข้อพิพาท
        var admin = new User
        {
            DisplayName = "แอดมินสุวิจักขณ์",
            Email = "admin@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Admin,
            SellerStatus = SellerStatuses.None,
            CreatedAt = now.AddDays(-60)
        };

        // บัญชีฝ่ายการเงิน (Finance) สำหรับตรวจสลิป ปล่อยเงิน Escrow คืนเงิน และตั้งค่าอัตราการเงิน
        var finance = new User
        {
            DisplayName = "ฝ่ายการเงินนิภา",
            Email = "finance@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Finance,
            SellerStatus = SellerStatuses.None,
            CreatedAt = now.AddDays(-60)
        };

        // บันทึกผู้ใช้ทั้งหมดลงฐานข้อมูลก่อนเพื่อให้ได้ค่า User.Id สำหรับใช้เป็น Foreign Key ในตารางถัดไป
        db.Users.AddRange(buyer1, buyer2, seller1, seller2, pendingSeller, admin, finance);
        await db.SaveChangesAsync();

        // --------------------------------------------------------------------
        // 3. สร้างหมวดหมู่สินค้าเริ่มต้น 4 หมวดหมู่
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ส่งไปแสดงแถบตัวกรองหมวดหมู่ในหน้าแรกผ่าน GET /api/auctions
        // --------------------------------------------------------------------
        var catKitchen = new Category { Name = "เครื่องครัว", Symbol = "♨", Description = "มีดตีมือ กระทะเหล็กหล่อ และเครื่องครัววินเทจ", IsActive = true };
        var catTea = new Category { Name = "ชาและเซรามิก", Symbol = "◌", Description = "ชุดชงชา ถ้วยลายคราม และเครื่องปั้นดินเผา", IsActive = true };
        var catCoin = new Category { Name = "เหรียญสะสม", Symbol = "◎", Description = "เหรียญหายาก ธนบัตร และของสะสมทางประวัติศาสตร์", IsActive = true };
        var catDecor = new Category { Name = "ของแต่งบ้าน", Symbol = "⌂", Description = "นาฬิกา โคมไฟ แจกัน และของตกแต่งโบราณ", IsActive = true };

        // บันทึกหมวดหมู่ลงฐานข้อมูลเพื่อให้ได้ Category.Id สำหรับผูกกับ AuctionItem
        db.Categories.AddRange(catKitchen, catTea, catCoin, catDecor);
        await db.SaveChangesAsync();

        // --------------------------------------------------------------------
        // 4. สร้างรายการสินค้าประมูลตัวอย่าง (แบ่งเป็น 3 กลุ่ม: กำลังประมูล, รออนุมัติ, ปิดประมูลแล้ว)
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน]
        //    - สินค้ากลุ่ม Approved แสดงบนหน้าตลาดประมูลผ่าน GET /api/auctions (wwwroot/js/site.js)
        //    - สินค้ากลุ่ม Pending แสดงในคิวตรวจอนุมัติของแอดมินผ่าน GET /api/admin/dashboard
        //    - สินค้ากลุ่ม Closed ใช้แสดงประวัติคำสั่งซื้อ ส่วนลดหมวดเดียวกัน และข้อพิพาท
        // --------------------------------------------------------------------

        // 4.1 สินค้าที่ได้รับอนุมัติและกำลังเปิดประมูลอยู่ (Live Approved Auctions)
        var knife = new AuctionItem
        {
            Name = "มีดครัวญี่ปุ่นตีมือ",
            CategoryId = catKitchen.Id,
            SellerId = seller1.Id,
            Condition = "สภาพดี · ราวปี 1970",
            Description = "มีดครัวญี่ปุ่นงานตีมือ ใบมีดเหล็กคาร์บอน ลับคมใหม่พร้อมใช้งาน ด้ามไม้เดิมมีร่องรอยตามอายุ เหมาะกับนักสะสมเครื่องครัววินเทจ",
            ImageUrl = "https://images.unsplash.com/photo-1593618998160-e34014e67546?auto=format&fit=crop&w=800&q=80",
            StartPrice = 800,
            CurrentPrice = 1250,
            MinIncrement = 100,
            StartAt = now.AddHours(-6),
            EndAt = now.AddHours(3), // เหลือเวลาประมูลอีก 3 ชม.
            Status = AuctionStatuses.Approved,
            CreatedAt = now.AddHours(-8)
        };

        var teaSet = new AuctionItem
        {
            Name = "ชุดชงชาเซรามิกญี่ปุ่น",
            CategoryId = catTea.Id,
            SellerId = seller2.Id,
            Condition = "สภาพดีมาก · ราวปี 1960",
            Description = "ชุดชงชาประกอบด้วยกาน้ำชาและถ้วย 4 ใบ เคลือบสีงาช้างลายคราม งานทำมือจากญี่ปุ่น ไม่มีรอยร้าวหรือบิ่น",
            ImageUrl = "https://images.unsplash.com/photo-1578749556568-bc2c40e68b61?auto=format&fit=crop&w=800&q=80",
            StartPrice = 1800,
            CurrentPrice = 2800,
            MinIncrement = 200,
            StartAt = now.AddHours(-5),
            EndAt = now.AddHours(6), // เหลือเวลาประมูลอีก 6 ชม.
            Status = AuctionStatuses.Approved,
            CreatedAt = now.AddHours(-6)
        };

        var coin = new AuctionItem
        {
            Name = "เหรียญ 1 บาท ร.5 พ.ศ. 2451",
            CategoryId = catCoin.Id,
            SellerId = seller1.Id,
            Condition = "ผ่านการใช้งาน · หายาก",
            Description = "เหรียญเงินรัชกาลที่ 5 พ.ศ. 2451 สภาพผ่านการใช้งานตามอายุ รายละเอียดด้านหน้าและหลังยังชัดเจน มีภาพถ่ายขยายให้ตรวจสอบ",
            ImageUrl = "https://images.unsplash.com/photo-1605100804763-247f67b3557e?auto=format&fit=crop&w=800&q=80",
            StartPrice = 1200,
            CurrentPrice = 1950,
            MinIncrement = 50,
            StartAt = now.AddHours(-10),
            EndAt = now.AddMinutes(55), // ใกล้ปิดประมูล (อีก 55 นาที) เพื่อให้เห็นป้ายใกล้หมดเวลาบนหน้าบ้าน
            Status = AuctionStatuses.Approved,
            CreatedAt = now.AddHours(-12)
        };

        var vase = new AuctionItem
        {
            Name = "แจกันเซรามิกลายคราม",
            CategoryId = catDecor.Id,
            SellerId = seller2.Id,
            Condition = "สภาพดี · มีอายุราว 50 ปี",
            Description = "แจกันเซรามิกลายครามทรงคลาสสิก เคลือบเงา ลวดลายเขียนมือ ไม่มีรอยร้าว เหมาะสำหรับจัดแสดงหรือสะสม",
            ImageUrl = "https://images.unsplash.com/photo-1578500494198-246f612d3b3d?auto=format&fit=crop&w=800&q=80",
            StartPrice = 1000,
            CurrentPrice = 1600,
            MinIncrement = 100,
            StartAt = now.AddHours(-2),
            EndAt = now.AddHours(10), // เหลือเวลาประมูลอีก 10 ชม.
            Status = AuctionStatuses.Approved,
            CreatedAt = now.AddHours(-3)
        };

        // 4.2 สินค้าที่รออนุมัติจากแอดมิน (Pending Auctions for Admin review)
        var pendingPan = new AuctionItem
        {
            Name = "กระทะเหล็กหล่อโบราณ",
            CategoryId = catKitchen.Id,
            SellerId = seller1.Id,
            Condition = "ดี — มีร่องรอยตามอายุ",
            Description = "กระทะเหล็กหล่อหนาพิเศษ ผลิตยุค 1975 ผ่านการปรุงผิวเคลือบน้ำมันแล้ว พร้อมใช้งานหรือสะสม",
            ImageUrl = "https://images.unsplash.com/photo-1590794056226-79ef3a8147e1?auto=format&fit=crop&w=800&q=80",
            StartPrice = 650,
            CurrentPrice = 650,
            MinIncrement = 50,
            StartAt = now.AddHours(1),
            EndAt = now.AddDays(2),
            Status = AuctionStatuses.Pending,
            CreatedAt = now.AddHours(-2)
        };

        var pendingCups = new AuctionItem
        {
            Name = "ถ้วยชาลายครามคู่",
            CategoryId = catTea.Id,
            SellerId = seller2.Id,
            Condition = "ดีมาก — เก็บสะสม",
            Description = "ถ้วยชาลายครามเขียนมือ 2 ใบเข้าชุด เก็บในตู้กระจกตลอด สภาพสมบูรณ์มาก",
            ImageUrl = "https://images.unsplash.com/photo-1578749556568-bc2c40e68b61?auto=format&fit=crop&w=800&q=80",
            StartPrice = 900,
            CurrentPrice = 900,
            MinIncrement = 50,
            StartAt = now.AddHours(2),
            EndAt = now.AddDays(3),
            Status = AuctionStatuses.Pending,
            CreatedAt = now.AddHours(-1)
        };

        // 4.3 สินค้าที่ปิดประมูลแล้ว (Closed Auctions — ใช้จำลองประวัติการซื้อ ส่วนลดหมวดเดียวกัน และกรณีข้อพิพาท)
        var closedTea1 = new AuctionItem
        {
            Name = "กาน้ำชาดินเผาจื่อซา",
            CategoryId = catTea.Id,
            SellerId = seller2.Id,
            Condition = "ดีมาก — เก็บสะสม",
            Description = "กาน้ำชาดินเผาแท้ ทรงคลาสสิก อายุราว 40 ปี ไม่มีตำหนิ",
            ImageUrl = "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80",
            StartPrice = 1500,
            CurrentPrice = 2400,
            MinIncrement = 100,
            StartAt = now.AddDays(-5),
            EndAt = now.AddDays(-3),
            Status = AuctionStatuses.Closed,
            WinnerId = buyer1.Id, // ผู้ซื้อคนที่ 1 ชนะและชำระเงินแล้ว (ทำให้มีสิทธิ์รับส่วนลดหมวดชาและเซรามิกในชิ้นถัดไป)
            CreatedAt = now.AddDays(-6)
        };

        var closedTea2 = new AuctionItem
        {
            Name = "ถ้วยชาลายครามวินเทจ",
            CategoryId = catTea.Id,
            SellerId = seller2.Id,
            Condition = "ดี — มีร่องรอยตามอายุ",
            Description = "ถ้วยชาลายครามวินเทจจากเกียวโต สวยคมชัด",
            ImageUrl = "https://images.unsplash.com/photo-1578749556568-bc2c40e68b61?auto=format&fit=crop&w=800&q=80",
            StartPrice = 1000,
            CurrentPrice = 1800,
            MinIncrement = 100,
            StartAt = now.AddDays(-2),
            EndAt = now.AddHours(-4),
            Status = AuctionStatuses.Closed,
            WinnerId = buyer1.Id, // ผู้ซื้อคนที่ 1 ชนะชิ้นที่ 2 ในหมวดเดียวกัน (รอชำระเงินพร้อมส่วนลด 10%)
            CreatedAt = now.AddDays(-3)
        };

        var closedClock = new AuctionItem
        {
            Name = "นาฬิกาตั้งโต๊ะไขลานยุค 1965",
            CategoryId = catDecor.Id,
            SellerId = seller1.Id,
            Condition = "ผ่านการใช้งาน",
            Description = "นาฬิกาไขลานตัวเรือนทองเหลือง เดินตรง มีเสียงตีระฆัง",
            ImageUrl = "https://images.unsplash.com/photo-1509048191080-d2984bad6ae5?auto=format&fit=crop&w=800&q=80",
            StartPrice = 2000,
            CurrentPrice = 3200,
            MinIncrement = 100,
            StartAt = now.AddDays(-4),
            EndAt = now.AddDays(-1),
            Status = AuctionStatuses.Closed,
            WinnerId = buyer2.Id, // ผู้ซื้อคนที่ 2 ชนะประมูล (มีข้อพิพาทเปิดไว้เพื่อสาธิตงานแอดมินและการเงิน)
            CreatedAt = now.AddDays(-5)
        };

        // บันทึกรายการสินค้าประมูลทั้งหมดก่อนเพื่อให้ได้ AuctionItem.Id สำหรับสร้าง Bid, SavedItem, OrderPayment และ Conversation
        db.AuctionItems.AddRange(knife, teaSet, coin, vase, pendingPan, pendingCups, closedTea1, closedTea2, closedClock);
        await db.SaveChangesAsync();

        // --------------------------------------------------------------------
        // 5. สร้างประวัติการเสนอราคา (Bids) และรายการบันทึกสนใจ (SavedItems)
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] แสดงในรายละเอียดสินค้า (GET /api/auctions/{id}) และแดชบอร์ดผู้ซื้อ (GET /api/buyer/dashboard)
        // --------------------------------------------------------------------
        db.Bids.AddRange(
            new Bid { AuctionItemId = knife.Id, BidderId = buyer2.Id, Amount = 1050, CreatedAt = now.AddHours(-3) },
            new Bid { AuctionItemId = knife.Id, BidderId = buyer1.Id, Amount = 1150, CreatedAt = now.AddHours(-2) },
            new Bid { AuctionItemId = knife.Id, BidderId = buyer2.Id, Amount = 1250, CreatedAt = now.AddHours(-1) },
            new Bid { AuctionItemId = teaSet.Id, BidderId = buyer2.Id, Amount = 2600, CreatedAt = now.AddHours(-2) },
            new Bid { AuctionItemId = teaSet.Id, BidderId = buyer1.Id, Amount = 2800, CreatedAt = now.AddMinutes(-45) },
            new Bid { AuctionItemId = coin.Id, BidderId = buyer2.Id, Amount = 1900, CreatedAt = now.AddMinutes(-35) },
            new Bid { AuctionItemId = coin.Id, BidderId = buyer1.Id, Amount = 1950, CreatedAt = now.AddMinutes(-20) },
            new Bid { AuctionItemId = vase.Id, BidderId = buyer2.Id, Amount = 1600, CreatedAt = now.AddMinutes(-50) },
            new Bid { AuctionItemId = closedTea1.Id, BidderId = buyer1.Id, Amount = 2400, CreatedAt = now.AddDays(-3).AddHours(-1) },
            new Bid { AuctionItemId = closedTea2.Id, BidderId = buyer1.Id, Amount = 1800, CreatedAt = now.AddHours(-5) },
            new Bid { AuctionItemId = closedClock.Id, BidderId = buyer2.Id, Amount = 3200, CreatedAt = now.AddDays(-1).AddHours(-2) }
        );

        // จำลองว่า buyer1 กดบันทึกรายการ "มีดครัวญี่ปุ่นตีมือ" ไว้ในรายการที่สนใจ
        db.SavedItems.Add(new SavedItem { UserId = buyer1.Id, AuctionItemId = knife.Id, CreatedAt = now.AddHours(-2) });

        // --------------------------------------------------------------------
        // 6. สร้างคำสั่งซื้อและการชำระเงินตัวอย่าง 3 สถานะ (OrderPayments)
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] ข้อมูลชุดนี้แสดงในหน้าชำระเงินผู้ซื้อ (GET /api/buyer/dashboard),
        //    หน้ายอดขายผู้ขาย (GET /api/seller/dashboard) และหน้าฝ่ายการเงิน (GET /api/finance/dashboard)
        // --------------------------------------------------------------------

        // คำสั่งซื้อที่ 1: ปิดงานสมบูรณ์ (ชำระแล้ว + นัดรับเสร็จ + ฝ่ายการเงินปล่อยเงินให้ผู้ขายแล้ว)
        var order1 = new OrderPayment
        {
            OrderCode = "AUC-0001",
            AuctionItemId = closedTea1.Id,
            BuyerId = buyer1.Id,
            SellerId = seller2.Id,
            WinningPrice = 2400,
            DiscountPercent = 0,
            DiscountAmount = 0,
            FinalBuyerAmount = 2400,
            PlatformFeePercent = 5,
            PlatformFeeAmount = 120,
            SellerNetAmount = 2280,
            PaymentStatus = PaymentStatuses.Paid,
            PaidAt = now.AddDays(-3),
            MeetupStatus = MeetupStatuses.Completed,
            MeetupLocation = "BTS อารีย์ ทางออก 3",
            MeetupAt = now.AddDays(-2),
            MeetupNote = "นัดรับหน้าร้านกาแฟ",
            DeliveredAt = now.AddDays(-2),
            ReceivedAt = now.AddDays(-2).AddMinutes(15),
            PayoutStatus = PayoutStatuses.Released,
            PayoutReleasedAt = now.AddDays(-1),
            FinanceNote = "โอนเงินให้ผู้ขายเรียบร้อยแล้วหลังผู้ซื้อยืนยันรับสินค้า",
            CreatedAt = now.AddDays(-3)
        };

        // คำสั่งซื้อที่ 2: รอชำระเงินของ buyer1 ในหมวดเดียวกัน ("ชาและเซรามิก") -> ได้ส่วนลดซื้อหมวดเดียวกัน 10% (เหลือ ฿1,620)
        var order2 = new OrderPayment
        {
            OrderCode = "AUC-0002",
            AuctionItemId = closedTea2.Id,
            BuyerId = buyer1.Id,
            SellerId = seller2.Id,
            WinningPrice = 1800,
            DiscountPercent = 10,
            DiscountReason = "ส่วนลดซื้อหมวดเดียวกัน 10% (มีรายการในหมวดนี้แล้ว 1 ชิ้น)",
            DiscountAmount = 180,
            FinalBuyerAmount = 1620,
            PlatformFeePercent = 5,
            PlatformFeeAmount = 81,
            SellerNetAmount = 1539,
            PaymentStatus = PaymentStatuses.Unpaid,
            MeetupStatus = MeetupStatuses.NotScheduled,
            PayoutStatus = PayoutStatuses.Pending,
            CreatedAt = now.AddHours(-4)
        };

        // คำสั่งซื้อที่ 3: ชำระเงินแล้ว + ผู้ขายส่งมอบแล้ว (Delivered) แต่ยังรอปล่อยเงิน Escrow และมีข้อพิพาทเปิดค้างอยู่เพื่อสาธิตการบล็อกปล่อยเงิน
        var order3 = new OrderPayment
        {
            OrderCode = "AUC-0003",
            AuctionItemId = closedClock.Id,
            BuyerId = buyer2.Id,
            SellerId = seller1.Id,
            WinningPrice = 3200,
            DiscountPercent = 0,
            DiscountAmount = 0,
            FinalBuyerAmount = 3200,
            PlatformFeePercent = 5,
            PlatformFeeAmount = 160,
            SellerNetAmount = 3040,
            PaymentStatus = PaymentStatuses.Paid,
            PaidAt = now.AddHours(-20),
            MeetupStatus = MeetupStatuses.Delivered,
            MeetupLocation = "MRT สวนจตุจักร ประตู 2",
            MeetupAt = now.AddHours(-6),
            MeetupNote = "ตรวจสอบเสียงตีระฆังก่อนรับสินค้า",
            DeliveredAt = now.AddHours(-5),
            PayoutStatus = PayoutStatuses.Pending,
            CreatedAt = now.AddDays(-1)
        };

        // บันทึกคำสั่งซื้อลงฐานข้อมูลก่อนเพื่อให้ได้ OrderPayment.Id สำหรับผูกกับ Dispute และ Notification
        db.OrderPayments.AddRange(order1, order2, order3);
        await db.SaveChangesAsync();

        // --------------------------------------------------------------------
        // 7. สร้างห้องสนทนาและข้อความแชทตัวอย่าง (Conversations & ChatMessages)
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] หน้าบ้านดึงแสดงในหน้าข้อความผ่าน GET /api/chat/conversations
        //    และดึงรายละเอียดข้อความผ่าน GET /api/chat/conversations/{id}/messages
        // --------------------------------------------------------------------

        // ห้องแชท 1: สอบถามสภาพมีดก่อนปิดประมูล (แชทปกติ แอดมินไม่มีสิทธิ์เข้าดู)
        var convKnife = new Conversation
        {
            AuctionItemId = knife.Id,
            BuyerId = buyer1.Id,
            SellerId = seller1.Id,
            CreatedAt = now.AddHours(-4),
            UpdatedAt = now.AddHours(-1),
            Messages =
            [
                new ChatMessage { SenderId = buyer1.Id, Body = "สวัสดีค่ะ สนใจมีดชิ้นนี้อยู่ อยากทราบว่ามีรอยบิ่นตรงคมหรือเปล่าคะ?", SentAt = now.AddHours(-3) },
                new ChatMessage { SenderId = seller1.Id, Body = "สวัสดีครับ ไม่มีรอยบิ่นเลยครับ มีแค่รอยใช้งานตามอายุ ลับคมเรียบร้อยแล้วครับ", SentAt = now.AddHours(-2) },
                new ChatMessage { SenderId = buyer1.Id, Body = "ขอบคุณค่ะ ถ้ารอบนี้ชนะสะดวกนัดรับวันเสาร์แถวจตุจักรไหมคะ?", SentAt = now.AddHours(-1) }
            ]
        };

        // ห้องแชท 2: แจ้งแสดงความยินดีหลังชนะประมูลถ้วยชาและเตรียมนัดรับ
        var convTea = new Conversation
        {
            AuctionItemId = closedTea2.Id,
            BuyerId = buyer1.Id,
            SellerId = seller2.Id,
            CreatedAt = now.AddHours(-4),
            UpdatedAt = now.AddMinutes(-30),
            Messages =
            [
                new ChatMessage { SenderId = seller2.Id, Body = "ยินดีด้วยนะคะ คุณพิมพ์ชนกชนะถ้วยชาลายครามวินเทจค่ะ ชำระเงินแล้วทักนัดรับได้เลยนะคะ", SentAt = now.AddHours(-4) },
                new ChatMessage { SenderId = buyer1.Id, Body = "ขอบคุณค่ะ เดี๋ยวชำระเงินในระบบแล้วนัดรับที่ BTS อารีย์เหมือนเดิมนะคะ", SentAt = now.AddMinutes(-30) }
            ]
        };

        // ห้องแชท 3: ห้องสนทนาของนาฬิกาไขลานที่มีการแจ้งปัญหาและเปิดข้อพิพาท (แอดมินดูข้อความห้องนี้ได้ผ่านหน้าข้อพิพาท)
        var convClock = new Conversation
        {
            AuctionItemId = closedClock.Id,
            BuyerId = buyer2.Id,
            SellerId = seller1.Id,
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddHours(-3),
            Messages =
            [
                new ChatMessage { SenderId = seller1.Id, Body = "ผมกำหนดนัดรับไว้ที่ MRT สวนจตุจักรแล้วนะครับ", SentAt = now.AddHours(-12) },
                new ChatMessage { SenderId = buyer2.Id, Body = "รับของมาแล้วครับ แต่ลองไขลานดูปรากฏว่าเสียงระฆังไม่ตีตามที่แจ้งในรายละเอียดครับ", SentAt = now.AddHours(-4) },
                new ChatMessage { SenderId = seller1.Id, Body = "ต้องหมุนปุ่มปลดล็อกด้านหลังก่อนนะครับ ถ้ายังไม่ดังยินดีตรวจเช็กหรือคืนเงินครับ", SentAt = now.AddHours(-3) }
            ]
        };

        db.Conversations.AddRange(convKnife, convTea, convClock);
        await db.SaveChangesAsync();

        // --------------------------------------------------------------------
        // 8. สร้างข้อพิพาทตัวอย่าง (Dispute) ผูกกับห้องแชท convClock และคำสั่งซื้อ order3
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] แอดมินตรวจสอบผ่าน GET /api/admin/dashboard และ GET /api/admin/disputes/{id}/messages
        //    (ออกแบบตามหลักความเป็นส่วนตัว: แอดมินตรวจสอบข้อความแชทได้เฉพาะห้องที่มีข้อพิพาทเท่านั้น)
        // --------------------------------------------------------------------
        db.Disputes.Add(new Dispute
        {
            ConversationId = convClock.Id,
            OrderPaymentId = order3.Id,
            ReporterId = buyer2.Id,
            Reason = "สินค้าไม่ตรงตามรายละเอียด",
            Details = "นาฬิกาไขลานรับมาแล้วเสียงตีระฆังไม่ทำงาน ขอให้แอดมินช่วยตรวจสอบบทสนทนาและประสานงานครับ",
            Status = DisputeStatuses.Open,
            CreatedAt = now.AddHours(-2)
        });

        // --------------------------------------------------------------------
        // 9. สร้างการแจ้งเตือนเริ่มต้น (Initial Notifications) สำหรับผู้ใช้ตัวอย่าง
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] หน้าบ้าน (wwwroot/js/site.js) ดึงไปแสดงที่ไอคอนกระดิ่งและจุดแดงแจ้งเตือน
        //    ผ่านการ Polling ไปที่ GET /api/notifications/summary (NotificationsController)
        // --------------------------------------------------------------------
        db.Notifications.AddRange(
            new Notification
            {
                UserId = buyer1.Id,
                Type = "payment",
                Title = "ยินดีด้วย! คุณชนะการประมูลถ้วยชาลายครามวินเทจ",
                Body = "ได้รับส่วนลดซื้อหมวดเดียวกัน 10% เหลือ ฿1,620 คลิกเพื่อชำระเงิน",
                TargetView = "payments", // คลิกแล้วหน้าบ้านสลับไปหน้าชำระเงินทันที
                ReferenceId = order2.Id,
                IsRead = false,
                CreatedAt = now.AddHours(-4)
            },
            new Notification
            {
                UserId = buyer1.Id,
                Type = "message",
                Title = "ข้อความใหม่จาก บ้านชาและครามรัตนา",
                Body = "ยินดีด้วยนะคะ คุณพิมพ์ชนกชนะถ้วยชาลายครามวินเทจค่ะ ชำระเงินแล้วทักนัดรับได้เลยนะคะ",
                TargetView = "messages", // คลิกแล้วหน้าบ้านเปิดห้องแชท convTea
                ReferenceId = convTea.Id,
                IsRead = false,
                CreatedAt = now.AddMinutes(-30)
            },
            new Notification
            {
                UserId = seller1.Id,
                Type = "message",
                Title = "ข้อความใหม่จาก คุณพิมพ์ชนก (ผู้ประมูล)",
                Body = "ขอบคุณค่ะ ถ้ารอบนี้ชนะสะดวกนัดรับวันเสาร์แถวจตุจักรไหมคะ?",
                TargetView = "messages", // คลิกแล้วหน้าบ้านเปิดห้องแชท convKnife
                ReferenceId = convKnife.Id,
                IsRead = false,
                CreatedAt = now.AddHours(-1)
            },
            new Notification
            {
                UserId = admin.Id,
                Type = "dispute",
                Title = "มีข้อพิพาทใหม่รอตรวจสอบ",
                Body = "คุณปกรณ์ นักสะสม แจ้งเรื่อง: สินค้าไม่ตรงตามรายละเอียด (นาฬิกาตั้งโต๊ะไขลานยุค 1965)",
                TargetView = "admin-disputes", // คลิกแล้วหน้าบ้านเปิดหน้าจัดการข้อพิพาทของแอดมิน
                IsRead = false,
                CreatedAt = now.AddHours(-2)
            }
        );

        // บันทึกข้อพิพาทและการแจ้งเตือนทั้งหมดลงฐานข้อมูล SQLite
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------------
    // เมธอดช่วยอัปเกรดฐานข้อมูล SQLite รุ่นเก่า (Legacy Database Upgrade) และรัน EF Core Migrations
    // เหตุผล: ในช่วงแรกของโปรเจกต์อาจสร้างไฟล์ auction.db ด้วย EnsureCreated() ซึ่งไม่มีตาราง __EFMigrationsHistory
    //         หากรัน MigrateAsync() ทันทีจะชนว่าตาราง Users มีอยู่แล้ว เมธอดนี้จึงช่วยเติมตาราง/คอลัมน์ใหม่ที่ขาด
    //         แล้วบันทึก Baseline Migration เพื่อให้ใช้งาน EF Core Migrations ต่อได้อย่างราบรื่นโดยไม่เสียข้อมูลเดิม
    // ------------------------------------------------------------------------
    private static async Task MigrateOrUpgradeLegacyDbAsync(AppDbContext db)
    {
        // ดึงการเชื่อมต่อระดับต่ำ (DbConnection) ของ SQLite ออกมาตรวจสอบโครงสร้างตารางโดยตรง
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync();
        try
        {
            // ฟังก์ชันย่อยเช็กว่ามีตารางชื่อที่กำหนดอยู่ในฐานข้อมูล SQLite แล้วหรือไม่ (ผ่านตารางระบบ sqlite_master)
            async Task<bool> TableExistsAsync(string name)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n;";
                var p = cmd.CreateParameter();
                p.ParameterName = "$n";
                p.Value = name;
                cmd.Parameters.Add(p);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
            }

            var hasUsers = await TableExistsAsync("Users");
            var hasHistory = await TableExistsAsync("__EFMigrationsHistory");

            // กรณีพบว่ามีตาราง Users อยู่แล้ว แต่ยังไม่มีตาราง __EFMigrationsHistory (ฐานข้อมูลเก่าจาก EnsureCreated)
            // ให้ซิงก์ตาราง คอลัมน์ และ Index ใหม่ให้ตรงกับ Schema ปัจจุบัน แล้วสร้าง Baseline History ครั้งเดียว
            if (hasUsers && !hasHistory)
            {
                // ฟังก์ชันย่อยเช็กคอลัมน์ด้วย PRAGMA table_info และเพิ่มคอลัมน์ใหม่ด้วย ALTER TABLE หากยังไม่มี
                async Task EnsureColumnAsync(string table, string column, string sqlType)
                {
                    var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var p = conn.CreateCommand())
                    {
                        p.CommandText = $"PRAGMA table_info(\"{table}\");";
                        using var r = await p.ExecuteReaderAsync();
                        // คอลัมน์ index 1 ของผลลัพธ์ PRAGMA table_info คือชื่อคอลัมน์ (name)
                        while (await r.ReadAsync()) cols.Add(r.GetString(1));
                    }
                    // เพิ่มคอลัมน์เฉพาะเมื่อมีตารางนั้นอยู่จริงและยังไม่มีคอลัมน์ดังกล่าว
                    if (cols.Count > 0 && !cols.Contains(column))
                    {
                        using var a = conn.CreateCommand();
                        a.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType};";
                        await a.ExecuteNonQueryAsync();
                    }
                }

                // สร้างตารางใหม่ที่เพิ่มเข้ามาภายหลัง (Notifications และ AuditLogs) พร้อม Index หากยังไม่มี
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        CREATE TABLE IF NOT EXISTS "Notifications" (
                            "Id" INTEGER NOT NULL CONSTRAINT "PK_Notifications" PRIMARY KEY AUTOINCREMENT,
                            "UserId" INTEGER NOT NULL,
                            "Type" TEXT NOT NULL,
                            "Title" TEXT NOT NULL,
                            "Body" TEXT NOT NULL,
                            "TargetView" TEXT NULL,
                            "ReferenceId" INTEGER NULL,
                            "IsRead" INTEGER NOT NULL,
                            "CreatedAt" TEXT NOT NULL,
                            CONSTRAINT "FK_Notifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                        );
                        CREATE INDEX IF NOT EXISTS "IX_Notifications_UserId_IsRead_CreatedAt" ON "Notifications" ("UserId", "IsRead", "CreatedAt");

                        CREATE TABLE IF NOT EXISTS "AuditLogs" (
                            "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
                            "ActorId" INTEGER NOT NULL,
                            "ActorName" TEXT NOT NULL,
                            "ActorRole" TEXT NOT NULL,
                            "Action" TEXT NOT NULL,
                            "TargetType" TEXT NOT NULL,
                            "TargetId" INTEGER NULL,
                            "Details" TEXT NOT NULL,
                            "CreatedAt" TEXT NOT NULL,
                            CONSTRAINT "FK_AuditLogs_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
                        );
                        CREATE INDEX IF NOT EXISTS "IX_AuditLogs_ActorId" ON "AuditLogs" ("ActorId");
                        CREATE INDEX IF NOT EXISTS "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");
                        """;
                    await cmd.ExecuteNonQueryAsync();
                }

                // เติมคอลัมน์ใหม่ที่เพิ่มเข้ามาในตารางเดิมทีละตาราง (แชทอ่านล่าสุด, วิธีชำระเงิน/สลิป/เส้นตาย, พร้อมเพย์กลาง, Concurrency Version, ล็อกอิน/รีเซ็ตรหัสผ่าน)
                await EnsureColumnAsync("Conversations", "BuyerLastReadAt", "TEXT NULL");
                await EnsureColumnAsync("Conversations", "SellerLastReadAt", "TEXT NULL");
                await EnsureColumnAsync("OrderPayments", "PaymentMethod", "TEXT NOT NULL DEFAULT 'PlatformEscrow'");
                await EnsureColumnAsync("OrderPayments", "PaymentSlipUrl", "TEXT NULL");
                await EnsureColumnAsync("OrderPayments", "PaymentNote", "TEXT NULL");
                await EnsureColumnAsync("OrderPayments", "PaymentSubmittedAt", "TEXT NULL");
                await EnsureColumnAsync("OrderPayments", "PaymentRejectReason", "TEXT NULL");
                await EnsureColumnAsync("OrderPayments", "PaymentDueAt", "TEXT NULL");
                await EnsureColumnAsync("FinanceSettings", "PlatformPromptPayId", "TEXT NOT NULL DEFAULT '081-999-8888'");
                await EnsureColumnAsync("FinanceSettings", "PlatformAccountName", "TEXT NOT NULL DEFAULT 'บจก. ของเก่าเล่าเรื่อง (บัญชีกลาง)'");
                await EnsureColumnAsync("AuctionItems", "Version", "INTEGER NOT NULL DEFAULT 1");
                await EnsureColumnAsync("Users", "FailedLoginCount", "INTEGER NOT NULL DEFAULT 0");
                await EnsureColumnAsync("Users", "LockoutEndAt", "TEXT NULL");
                await EnsureColumnAsync("Users", "PasswordResetToken", "TEXT NULL");
                await EnsureColumnAsync("Users", "PasswordResetExpiresAt", "TEXT NULL");

                // ปรับ Unique Index ของ OrderPayments ให้รองรับผู้ชนะอันดับถัดไป (AuctionItemId + BuyerId)
                // และสร้างตาราง __EFMigrationsHistory พร้อมบันทึก Baseline Migration '20261126135143_InitialSchema'
                using (var idx = conn.CreateCommand())
                {
                    idx.CommandText = """
                        DROP INDEX IF EXISTS "IX_OrderPayments_AuctionItemId";
                        CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderPayments_AuctionItemId_BuyerId" ON "OrderPayments" ("AuctionItemId", "BuyerId");
                        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                            "ProductVersion" TEXT NOT NULL
                        );
                        INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                        VALUES ('20261126135143_InitialSchema', '10.0.0');
                        """;
                    await idx.ExecuteNonQueryAsync();
                }
            }
        }
        finally
        {
            // ปิดการเชื่อมต่อหากเมธอดนี้เป็นผู้เปิดขึ้นมาเอง เพื่อไม่ให้ Connection ค้าง
            if (shouldClose) await conn.CloseAsync();
        }

        // รัน EF Core Migrations ตามมาตรฐาน (สร้างตารางทั้งหมดหากเป็นฐานข้อมูลใหม่ หรืออัปเดต Migration ในอนาคต)
        await db.Database.MigrateAsync();
    }
}
