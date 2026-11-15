using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class AdminNavigationReadStateEntityTypeConfiguration : IEntityTypeConfiguration<AdminNavigationReadState>
{
    public void Configure(EntityTypeBuilder<AdminNavigationReadState> builder)
    {
        builder.ToTable("NavigationReadStates", "admin");
        builder.HasKey(state => new { state.UserId, state.SectionCode });

        builder.Property(state => state.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(state => state.SectionCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(state => state.ViewedThroughUtc)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(state => state.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
