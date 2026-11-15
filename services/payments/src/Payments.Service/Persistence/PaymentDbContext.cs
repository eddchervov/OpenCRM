using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Service.Domain;

namespace Payments.Service.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentOrder> PaymentOrders => this.Set<PaymentOrder>();
    public DbSet<ProviderOperation> ProviderOperations => this.Set<ProviderOperation>();
    public DbSet<CommandReceipt> CommandReceipts => this.Set<CommandReceipt>();
    public DbSet<WebhookNotification> WebhookNotifications => this.Set<WebhookNotification>();
    public DbSet<PaymentAuditEntry> PaymentAuditEntries => this.Set<PaymentAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        var orders = modelBuilder.Entity<PaymentOrder>();
        orders.HasKey(order => order.Id);
        orders.HasIndex(order => new { order.ApplicationId, order.PurchaseId }).IsUnique();
        orders.HasIndex(order => new { order.MerchantAccountId, order.IsTest, order.ProviderPaymentId }).IsUnique();
        orders.HasIndex(order => order.NextCheckAt);
        orders.Property(order => order.ApplicationId).HasMaxLength(64);
        orders.Property(order => order.RequestHash).HasMaxLength(64);
        orders.Property(order => order.Currency).HasMaxLength(3);
        orders.Property(order => order.Description).HasMaxLength(128);
        orders.Property(order => order.ReturnUrl).HasMaxLength(2048);
        orders.Property(order => order.BuyerId).HasMaxLength(128);
        orders.Property(order => order.BeneficiaryId).HasMaxLength(128);
        orders.Property(order => order.MerchantAccountId).HasMaxLength(128);
        orders.Property(order => order.ProviderPaymentId).HasMaxLength(128);
        orders.Property(order => order.Status).HasConversion<string>().HasMaxLength(32);
        orders.Property(order => order.SucceededAt);
        orders.Property(order => order.ReconciliationFailureCount).HasDefaultValue(0);
        orders.ToTable("PaymentOrders", table =>
        {
            table.HasCheckConstraint("CK_PaymentOrders_Amount", "\"AmountMinor\" > 0");
            table.HasCheckConstraint("CK_PaymentOrders_Refund", "\"RefundedAmountMinor\" >= 0 AND \"RefundedAmountMinor\" <= \"AmountMinor\"");
            table.HasCheckConstraint("CK_PaymentOrders_Currency", "\"Currency\" = 'RUB'");
        });

        var operations = modelBuilder.Entity<ProviderOperation>();
        operations.HasKey(operation => operation.Id);
        operations.HasOne(operation => operation.PaymentOrder).WithMany(order => order.Operations)
            .HasForeignKey(operation => operation.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        operations.HasIndex(operation => operation.IdempotenceKey).IsUnique();
        operations.HasIndex(operation => new { operation.PaymentOrderId, operation.CommandId }).IsUnique();
        operations.HasIndex(operation => new { operation.Status, operation.NextAttemptAt });
        operations.Property(operation => operation.Kind).HasConversion<string>().HasMaxLength(32);
        operations.Property(operation => operation.ExecutionMode).HasMaxLength(32).HasDefaultValue("Disabled");
        operations.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(32);
        operations.Property(operation => operation.IdempotenceKey).HasMaxLength(64);
        operations.Property(operation => operation.PostBefore);
        operations.Property(operation => operation.ProviderObjectId).HasMaxLength(128);
        operations.Property(operation => operation.Reason).HasMaxLength(128);
        operations.Property(operation => operation.RequestedBy).HasMaxLength(128);
        operations.ToTable("ProviderOperations", table =>
            table.HasCheckConstraint("CK_ProviderOperations_Amount", "\"AmountMinor\" > 0"));

        var receipts = modelBuilder.Entity<CommandReceipt>();
        receipts.HasKey(receipt => new { receipt.ApplicationId, receipt.CommandId });
        receipts.Property(receipt => receipt.ApplicationId).HasMaxLength(64);
        receipts.Property(receipt => receipt.RequestHash).HasMaxLength(64);

        var webhooks = modelBuilder.Entity<WebhookNotification>();
        webhooks.HasKey(notification => notification.Id);
        webhooks.HasIndex(notification => new { notification.EventType, notification.ObjectId }).IsUnique();
        webhooks.HasIndex(notification => new { notification.ProcessedAt, notification.NextAttemptAt });
        webhooks.Property(notification => notification.EventType).HasMaxLength(64);
        webhooks.Property(notification => notification.ObjectId).HasMaxLength(128);

        var auditEntries = modelBuilder.Entity<PaymentAuditEntry>();
        auditEntries.HasKey(entry => entry.Id);
        auditEntries.HasOne<PaymentOrder>().WithMany().HasForeignKey(entry => entry.PaymentOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        auditEntries.HasOne<ProviderOperation>().WithMany().HasForeignKey(entry => entry.ProviderOperationId)
            .OnDelete(DeleteBehavior.Restrict);
        auditEntries.HasIndex(entry => new { entry.PaymentOrderId, entry.OccurredAt });
        auditEntries.Property(entry => entry.Stage).HasMaxLength(64);
        auditEntries.Property(entry => entry.Code).HasMaxLength(128);
        auditEntries.Property(entry => entry.ErrorType).HasMaxLength(128);
        auditEntries.ToTable("PaymentAuditEntries");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
