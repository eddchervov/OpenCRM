using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class UserActivityEventEntityTypeConfiguration : IEntityTypeConfiguration<UserActivityEvent>
{
    public void Configure(EntityTypeBuilder<UserActivityEvent> builder)
    {
        builder.ToTable("UserActivityEvents", "admin");
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.UserId).HasMaxLength(450).IsRequired();
        builder.Property(activity => activity.Kind).HasMaxLength(16).IsRequired();
        builder.Property(activity => activity.Action).HasMaxLength(80).IsRequired();
        builder.Property(activity => activity.Page).HasMaxLength(240).IsRequired();
        builder.Property(activity => activity.Scenario).HasMaxLength(32).IsRequired();
        builder.Property(activity => activity.Section).HasMaxLength(64).IsRequired();
        builder.Property(activity => activity.TargetId).HasMaxLength(64);
        builder.HasIndex(activity => new { activity.UserId, activity.Id });
        builder.HasIndex(activity => new { activity.OrganizationId, activity.Id });
        builder.HasIndex(activity => activity.ReceivedAt);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(activity => activity.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Organization>().WithMany().HasForeignKey(activity => activity.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
