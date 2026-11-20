using AppProject.Models;
using AppProject.Services;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Users.AnyAsync())
            return;

        var now = DateTime.UtcNow;

        var settings = new FinanceSettings
        {
            Id = 1,
            SameCategoryDiscountPercent = 10,
            PlatformFeePercent = 5,
            GlobalMinBidIncrement = 50,
            UpdatedAt = now
        };
        db.FinanceSettings.Add(settings);

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

        var admin = new User
        {
            DisplayName = "แอดมินสุวิจักขณ์",
            Email = "admin@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Admin,
            SellerStatus = SellerStatuses.None,
            CreatedAt = now.AddDays(-60)
        };

        var finance = new User
        {
            DisplayName = "ฝ่ายการเงินนิภา",
            Email = "finance@oldthings.th",
            PasswordHash = PasswordService.Hash("123456"),
            Role = Roles.Finance,
            SellerStatus = SellerStatuses.None,
            CreatedAt = now.AddDays(-60)
        };

        db.Users.AddRange(buyer1, buyer2, seller1, seller2, pendingSeller, admin, finance);
        await db.SaveChangesAsync();

        var catKitchen = new Category { Name = "เครื่องครัว", Symbol = "♨", Description = "มีดตีมือ กระทะเหล็กหล่อ และเครื่องครัววินเทจ", IsActive = true };
        var catTea = new Category { Name = "ชาและเซรามิก", Symbol = "◌", Description = "ชุดชงชา ถ้วยลายคราม และเครื่องปั้นดินเผา", IsActive = true };
        var catCoin = new Category { Name = "เหรียญสะสม", Symbol = "◎", Description = "เหรียญหายาก ธนบัตร และของสะสมทางประวัติศาสตร์", IsActive = true };
        var catDecor = new Category { Name = "ของแต่งบ้าน", Symbol = "⌂", Description = "นาฬิกา โคมไฟ แจกัน และของตกแต่งโบราณ", IsActive = true };

        db.Categories.AddRange(catKitchen, catTea, catCoin, catDecor);
        await db.SaveChangesAsync();

        // Live Approved Auctions
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
            EndAt = now.AddHours(3),
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
            EndAt = now.AddHours(6),
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
            EndAt = now.AddMinutes(55),
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
            EndAt = now.AddHours(10),
            Status = AuctionStatuses.Approved,
            CreatedAt = now.AddHours(-3)
        };

        // Pending Auctions for Admin review
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

        // Closed Auctions (Historical + Unpaid with Same-Category Discount!)
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
            WinnerId = buyer1.Id,
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
            WinnerId = buyer1.Id,
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
            WinnerId = buyer2.Id,
            CreatedAt = now.AddDays(-5)
        };

        db.AuctionItems.AddRange(knife, teaSet, coin, vase, pendingPan, pendingCups, closedTea1, closedTea2, closedClock);
        await db.SaveChangesAsync();

        // Bids
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

        db.SavedItems.Add(new SavedItem { UserId = buyer1.Id, AuctionItemId = knife.Id, CreatedAt = now.AddHours(-2) });

        // Orders / Payments
        // 1) Completed + Payout Released (Buyer1 bought Tea item #1)
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

        // 2) Unpaid order for Buyer1 in SAME category (ชาและเซรามิก) -> gets 10% same-category discount!
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

        // 3) Paid + Delivered (waiting for Finance release, AND has a sample Dispute for Admin & Finance demo!)
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

        db.OrderPayments.AddRange(order1, order2, order3);
        await db.SaveChangesAsync();

        // Conversations & Messages
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

        // Sample Dispute linked to convClock so Admin can inspect ONLY disputed chats
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

        await db.SaveChangesAsync();
    }
}
