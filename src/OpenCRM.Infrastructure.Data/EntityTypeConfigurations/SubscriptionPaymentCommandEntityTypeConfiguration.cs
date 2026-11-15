using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Долговечная доставка финансовых команд с арендой и внешним ключом покупки. </summary>
public sealed class SubscriptionPaymentCommandEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPaymentCommand>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentCommand> builder)
    {
        builder.ToTable("SubscriptionPaymentCommands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Kind).HasMaxLength(32);
        builder.Property(command => command.Payload).HasColumnType("text");
        builder.Property(command => command.PayloadHash).HasMaxLength(64);
        builder.Property(command => command.LastErrorCode).HasMaxLength(128);
        builder.HasIndex(command => new { command.SentAt, command.NextAttemptAt });
        builder.HasOne(command => command.Purchase).WithMany().HasForeignKey(command => command.PurchaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
