using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности пользователя приложения. </summary>
public class UsersEntityTypeConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <summary> Настраивает отображение сущности пользователя приложения. </summary>
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.CountVisits).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CountLogins).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CanUseTestSubscriptions).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.PersonalDataProcessingConsentAccepted).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.AdvertisingConsentAccepted).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.FirstUtmSource).HasMaxLength(120);
        builder.Property(x => x.FirstUtmMedium).HasMaxLength(120);
        builder.Property(x => x.FirstUtmCampaign).HasMaxLength(120);
        builder.Property(x => x.FirstUtmContent).HasMaxLength(120);
        builder.Property(x => x.FirstUtmTerm).HasMaxLength(120);
        builder.Property(x => x.LastUtmSource).HasMaxLength(120);
        builder.Property(x => x.LastUtmMedium).HasMaxLength(120);
        builder.Property(x => x.LastUtmCampaign).HasMaxLength(120);
        builder.Property(x => x.LastUtmContent).HasMaxLength(120);
        builder.Property(x => x.LastUtmTerm).HasMaxLength(120);
    }
}

