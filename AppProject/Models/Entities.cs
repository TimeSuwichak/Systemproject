using System.ComponentModel.DataAnnotations;

namespace AppProject.Models;

public static class Roles
{
    public const string Buyer = "Buyer";
    public const string Seller = "Seller";
    public const string Admin = "Admin";
    public const string Finance = "Finance";
}

public static class SellerStatuses
{
    public const string None = "None";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class AuctionStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Closed = "Closed";
}

public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string Paid = "Paid";
    public const string Refunded = "Refunded";
}

public static class MeetupStatuses
{
    public const string NotScheduled = "NotScheduled";
    public const string Scheduled = "Scheduled";
    public const string Delivered = "Delivered";
    public const string Completed = "Completed";
}

public static class PayoutStatuses
{
    public const string Pending = "Pending";
    public const string Released = "Released";
    public const string Refunded = "Refunded";
}

public static class DisputeStatuses
{
    public const string Open = "Open";
    public const string Investigating = "Investigating";
    public const string Resolved = "Resolved";
}

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Role { get; set; } = Roles.Buyer;

    public bool IsSuspended { get; set; }
    [MaxLength(300)]
    public string? SuspendReason { get; set; }

    [Required, MaxLength(20)]
    public string SellerStatus { get; set; } = SellerStatuses.None;

    [MaxLength(100)]
    public string? ShopName { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(120)]
    public string? BankAccount { get; set; }

    [MaxLength(500)]
    public string? SellerBio { get; set; }

    [MaxLength(300)]
    public string? SellerRejectReason { get; set; }

    public DateTime? SellerAppliedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Symbol { get; set; } = "✦";

    [MaxLength(200)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuctionItem
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int SellerId { get; set; }
    public User? Seller { get; set; }

    [Required, MaxLength(80)]
    public string Condition { get; set; } = string.Empty;

    [Required, MaxLength(1500)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    public decimal StartPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MinIncrement { get; set; } = 50;

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = AuctionStatuses.Pending;

    [MaxLength(400)]
    public string? RejectReason { get; set; }

    public int? WinnerId { get; set; }
    public User? Winner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Bid> Bids { get; set; } = [];
}

public class Bid
{
    public int Id { get; set; }

    public int AuctionItemId { get; set; }
    public AuctionItem? AuctionItem { get; set; }

    public int BidderId { get; set; }
    public User? Bidder { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SavedItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public int AuctionItemId { get; set; }
    public AuctionItem? AuctionItem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OrderPayment
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string OrderCode { get; set; } = string.Empty;

    public int AuctionItemId { get; set; }
    public AuctionItem? AuctionItem { get; set; }

    public int BuyerId { get; set; }
    public User? Buyer { get; set; }

    public int SellerId { get; set; }
    public User? Seller { get; set; }

    public decimal WinningPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    [MaxLength(160)]
    public string? DiscountReason { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalBuyerAmount { get; set; }

    public decimal PlatformFeePercent { get; set; }
    public decimal PlatformFeeAmount { get; set; }
    public decimal SellerNetAmount { get; set; }

    [Required, MaxLength(20)]
    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid;
    public DateTime? PaidAt { get; set; }

    [Required, MaxLength(20)]
    public string MeetupStatus { get; set; } = MeetupStatuses.NotScheduled;
    [MaxLength(200)]
    public string? MeetupLocation { get; set; }
    public DateTime? MeetupAt { get; set; }
    [MaxLength(300)]
    public string? MeetupNote { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    [Required, MaxLength(20)]
    public string PayoutStatus { get; set; } = PayoutStatuses.Pending;
    public DateTime? PayoutReleasedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    [MaxLength(300)]
    public string? FinanceNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Conversation
{
    public int Id { get; set; }

    public int AuctionItemId { get; set; }
    public AuctionItem? AuctionItem { get; set; }

    public int BuyerId { get; set; }
    public User? Buyer { get; set; }

    public int SellerId { get; set; }
    public User? Seller { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ChatMessage> Messages { get; set; } = [];
    public List<Dispute> Disputes { get; set; } = [];
}

public class ChatMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public int SenderId { get; set; }
    public User? Sender { get; set; }

    [Required, MaxLength(1500)]
    public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class Dispute
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public int? OrderPaymentId { get; set; }
    public OrderPayment? OrderPayment { get; set; }

    public int ReporterId { get; set; }
    public User? Reporter { get; set; }

    [Required, MaxLength(120)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(1500)]
    public string Details { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Status { get; set; } = DisputeStatuses.Open;

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public class FinanceSettings
{
    public int Id { get; set; } = 1;
    public decimal SameCategoryDiscountPercent { get; set; } = 10;
    public decimal PlatformFeePercent { get; set; } = 5;
    public decimal GlobalMinBidIncrement { get; set; } = 50;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
