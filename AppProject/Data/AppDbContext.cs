using AppProject.Models;
using Microsoft.EntityFrameworkCore;

namespace AppProject.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AuctionItem> AuctionItems => Set<AuctionItem>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<SavedItem> SavedItems => Set<SavedItem>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<FinanceSettings> FinanceSettings => Set<FinanceSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        modelBuilder.Entity<AuctionItem>(entity =>
        {
            entity.HasOne(a => a.Category)
                .WithMany()
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Seller)
                .WithMany()
                .HasForeignKey(a => a.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Winner)
                .WithMany()
                .HasForeignKey(a => a.WinnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Bid>(entity =>
        {
            entity.HasOne(b => b.AuctionItem)
                .WithMany(a => a.Bids)
                .HasForeignKey(b => b.AuctionItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.Bidder)
                .WithMany()
                .HasForeignKey(b => b.BidderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavedItem>(entity =>
        {
            entity.HasIndex(s => new { s.UserId, s.AuctionItemId }).IsUnique();
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.AuctionItem).WithMany().HasForeignKey(s => s.AuctionItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderPayment>(entity =>
        {
            entity.HasIndex(o => o.AuctionItemId).IsUnique();
            entity.HasIndex(o => o.OrderCode).IsUnique();
            entity.HasOne(o => o.AuctionItem).WithMany().HasForeignKey(o => o.AuctionItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.Seller).WithMany().HasForeignKey(o => o.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasIndex(c => new { c.AuctionItemId, c.BuyerId, c.SellerId }).IsUnique();
            entity.HasOne(c => c.AuctionItem).WithMany().HasForeignKey(c => c.AuctionItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.Buyer).WithMany().HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.Seller).WithMany().HasForeignKey(c => c.SellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.HasOne(d => d.Conversation).WithMany(c => c.Disputes).HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.OrderPayment).WithMany().HasForeignKey(d => d.OrderPaymentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.Reporter).WithMany().HasForeignKey(d => d.ReporterId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
