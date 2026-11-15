using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности предоставленной пользователю возможности. </summary>
public class UserFeatureGrantEntityTypeConfiguration : IEntityTypeConfiguration<UserFeatureGrant>
{
    /// <summary> Настраивает отображение сущности предоставленной возможности. </summary>
    public void Configure(EntityTypeBuilder<UserFeatureGrant> builder)
    {
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(500);

        builder.HasIndex(x => new { x.UserId, x.FeatureDefinitionId, x.IsEnabled });

        builder.HasOne(x => x.User)
            .WithMany(x => x.FeatureGrants)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FeatureDefinition)
            .WithMany(x => x.UserFeatureGrants)
            .HasForeignKey(x => x.FeatureDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

