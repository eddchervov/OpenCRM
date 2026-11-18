using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает ограничения сведений о последнем посещении пользователя. </summary>
public sealed class ApplicationUserEntityTypeConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <summary> Настраивает поля пользователя. </summary>
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.LastAccessIpAddress).HasMaxLength(45);
        builder.Property(user => user.LastAccessUserAgent).HasMaxLength(2048);
        builder.Property(user => user.LastAccessLanguage).HasMaxLength(512);
        builder.Property(user => user.PreferredLanguage).HasMaxLength(16);
        builder.Property(user => user.PreferredFormattingCulture).HasMaxLength(32);
        builder.Property(user => user.PersonalCurrencyCode).HasMaxLength(3).HasDefaultValue("RUB").IsRequired();
        builder.Property(user => user.LastAccessClientBrands).HasMaxLength(1024);
        builder.Property(user => user.LastAccessPlatform).HasMaxLength(256);
        builder.Property(user => user.LastAccessAuthenticationScheme).HasMaxLength(64);
        builder.Property(user => user.LastLoginMethod).HasMaxLength(32);
    }
}
