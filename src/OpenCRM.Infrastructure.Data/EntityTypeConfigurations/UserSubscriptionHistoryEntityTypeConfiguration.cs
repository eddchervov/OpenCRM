using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности истории подписки пользователя. </summary>
public sealed class UserSubscriptionHistoryEntityTypeConfiguration : IEntityTypeConfiguration<UserSubscriptionHistory>
{
    /// <summary> Настраивает отображение сущности истории подписки пользователя. </summary>
    public void Configure(EntityTypeBuilder<UserSubscriptionHistory> builder)
    {
        builder.Property(history => history.SourceCode)
            .HasMaxLength(16)
            .HasDefaultValue("unknown");
    }
}
