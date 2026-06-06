using Fulvero.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fulvero.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ProductionFile> ProductionFiles => Set<ProductionFile>();
    public DbSet<ProductSetting> ProductSettings => Set<ProductSetting>();
    public DbSet<ProductSupplierLink> ProductSupplierLinks => Set<ProductSupplierLink>();
    public DbSet<ProductionTask> ProductionTasks => Set<ProductionTask>();
    public DbSet<ProductionTaskItem> ProductionTaskItems => Set<ProductionTaskItem>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<SupplyItem> SupplyItems => Set<SupplyItem>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BillingPayment> BillingPayments => Set<BillingPayment>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TelegramIntegration> TelegramIntegrations => Set<TelegramIntegration>();
    public DbSet<TelegramNotificationState> TelegramNotificationStates => Set<TelegramNotificationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasIndex(company => company.LoginName).IsUnique();
            entity.Property(company => company.Name).HasMaxLength(180);
            entity.Property(company => company.LoginName).HasMaxLength(120);
            entity.Property(company => company.OzonClientIdProtected).HasMaxLength(2000);
            entity.Property(company => company.OzonApiKeyProtected).HasMaxLength(4000);
            entity.Property(company => company.SubscriptionStatus).HasMaxLength(32);
            entity.Property(company => company.LastYooKassaPaymentId).HasMaxLength(120);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(user => new { user.CompanyId, user.UserName }).IsUnique();
            entity.HasIndex(user => new { user.CompanyId, user.Email })
                .IsUnique()
                .HasFilter("\"Email\" <> ''");
            entity.Property(user => user.UserName).HasMaxLength(80);
            entity.Property(user => user.Email).HasMaxLength(180);
            entity.Property(user => user.DisplayName).HasMaxLength(160);
            entity.Property(user => user.Position).HasMaxLength(160);
            entity.Property(user => user.AvatarFileName).HasMaxLength(260);
            entity.Property(user => user.AllowedFeatures).HasMaxLength(2000);
            entity.Property(user => user.Role).HasMaxLength(32);
            entity.HasOne(user => user.Company)
                .WithMany()
                .HasForeignKey(user => user.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillingPayment>(entity =>
        {
            entity.HasIndex(payment => payment.PaymentId).IsUnique();
            entity.HasIndex(payment => payment.CompanyId);
            entity.Property(payment => payment.Provider).HasMaxLength(32);
            entity.Property(payment => payment.PaymentId).HasMaxLength(120);
            entity.Property(payment => payment.Status).HasMaxLength(32);
            entity.Property(payment => payment.Currency).HasMaxLength(8);
            entity.HasOne(payment => payment.Company)
                .WithMany()
                .HasForeignKey(payment => payment.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(128);
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelegramIntegration>(entity =>
        {
            entity.HasIndex(item => item.CompanyId).IsUnique();
            entity.HasIndex(item => item.LinkCode).IsUnique();
            entity.Property(item => item.LinkCode).HasMaxLength(80);
            entity.Property(item => item.ChatTitle).HasMaxLength(240);
            entity.HasOne(item => item.Company)
                .WithMany()
                .HasForeignKey(item => item.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelegramNotificationState>(entity =>
        {
            entity.HasIndex(item => new { item.CompanyId, item.NotificationKey }).IsUnique();
            entity.Property(item => item.NotificationKey).HasMaxLength(260);
            entity.HasOne(item => item.Company)
                .WithMany()
                .HasForeignKey(item => item.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductionFile>(entity =>
        {
            entity.HasIndex(file => file.CompanyId);
            entity.HasIndex(file => file.OfferId);
            entity.Property(file => file.OfferId).HasMaxLength(120);
            entity.Property(file => file.ProductName).HasMaxLength(240);
            entity.Property(file => file.FileName).HasMaxLength(260);
            entity.Property(file => file.ContentType).HasMaxLength(120);
            entity.HasOne(file => file.Company)
                .WithMany()
                .HasForeignKey(file => file.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductSetting>(entity =>
        {
            entity.HasIndex(item => new { item.CompanyId, item.OzonProductId }).IsUnique();
            entity.Property(item => item.OfferId).HasMaxLength(120);
            entity.Property(item => item.ProductName).HasMaxLength(240);
            entity.Property(item => item.ProductType).HasMaxLength(32);
            entity.HasOne(item => item.Company)
                .WithMany()
                .HasForeignKey(item => item.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductSupplierLink>(entity =>
        {
            entity.HasIndex(link => new { link.CompanyId, link.OzonProductId });
            entity.Property(link => link.OfferId).HasMaxLength(120);
            entity.Property(link => link.SupplierName).HasMaxLength(180);
            entity.Property(link => link.SupplierUrl).HasMaxLength(1000);
            entity.HasOne(link => link.Company)
                .WithMany()
                .HasForeignKey(link => link.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductionTask>(entity =>
        {
            entity.HasIndex(task => task.CompanyId);
            entity.HasIndex(task => task.Status);
            entity.HasIndex(task => task.IsArchived);
            entity.Property(task => task.OfferId).HasMaxLength(120);
            entity.Property(task => task.ProductName).HasMaxLength(240);
            entity.Property(task => task.Status).HasMaxLength(32);
            entity.Property(task => task.AssignedUserName).HasMaxLength(80);
            entity.HasOne(task => task.Company)
                .WithMany()
                .HasForeignKey(task => task.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(task => task.Items)
                .WithOne(item => item.ProductionTask)
                .HasForeignKey(item => item.ProductionTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductionTaskItem>(entity =>
        {
            entity.HasIndex(item => item.OfferId);
            entity.Property(item => item.OfferId).HasMaxLength(120);
            entity.Property(item => item.ProductName).HasMaxLength(240);
        });

        modelBuilder.Entity<Supply>(entity =>
        {
            entity.HasIndex(supply => supply.CompanyId);
            entity.HasIndex(supply => supply.Status);
            entity.HasIndex(supply => supply.IsArchived);
            entity.Property(supply => supply.Status).HasMaxLength(32);
            entity.HasOne(supply => supply.Company)
                .WithMany()
                .HasForeignKey(supply => supply.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(supply => supply.Items)
                .WithOne(item => item.Supply)
                .HasForeignKey(item => item.SupplyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplyItem>(entity =>
        {
            entity.HasIndex(item => item.OfferId);
            entity.HasIndex(item => item.IsReserve);
            entity.Property(item => item.OfferId).HasMaxLength(120);
            entity.Property(item => item.ProductName).HasMaxLength(240);
            entity.Property(item => item.SupplierName).HasMaxLength(180);
            entity.Property(item => item.SupplierUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(message => new { message.SenderId, message.ReceiverId, message.CreatedAt });
            entity.HasIndex(message => new { message.ReceiverId, message.SenderId, message.CreatedAt });
            entity.HasIndex(message => new { message.ReceiverId, message.ReadAt });
            entity.Property(message => message.Text).HasMaxLength(2000);
            entity.Property(message => message.AttachmentFileName).HasMaxLength(260);
            entity.Property(message => message.AttachmentContentType).HasMaxLength(120);
            entity.HasOne(message => message.Sender)
                .WithMany()
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(message => message.Receiver)
                .WithMany()
                .HasForeignKey(message => message.ReceiverId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(log => log.CompanyId);
            entity.HasIndex(log => log.CreatedAt);
            entity.HasIndex(log => log.Action);
            entity.HasIndex(log => log.EntityType);
            entity.HasIndex(log => log.UserName);
            entity.Property(log => log.UserName).HasMaxLength(80);
            entity.Property(log => log.DisplayName).HasMaxLength(160);
            entity.Property(log => log.Action).HasMaxLength(80);
            entity.Property(log => log.EntityType).HasMaxLength(80);
            entity.Property(log => log.EntityId).HasMaxLength(120);
            entity.Property(log => log.Details).HasMaxLength(2000);
            entity.HasOne(log => log.Company)
                .WithMany()
                .HasForeignKey(log => log.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
