using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация карточки техники. </summary>
public class VehicleEntityTypeConfiguration : IEntityTypeConfiguration<Vehicle>
{
    /// <summary> Настраивает модель карточки техники. </summary>
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.HasIndex(x => x.Kind);

        builder.HasIndex(x => x.MileageUnit);
        builder.HasIndex(x => x.CurrentMileageUpdatedAt);

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.OrganizationClientId);

        builder.Property(x => x.Vin)
            .HasMaxLength(32);

        builder.Property(x => x.RegistrationPlate)
            .HasMaxLength(24);

        builder.Property(x => x.RegistrationCountryCode)
            .HasMaxLength(2);

        builder.Property(x => x.BodyNumber)
            .HasMaxLength(64);

        builder.Property(x => x.EngineNumber)
            .HasMaxLength(64);

        builder.Property(x => x.Color)
            .HasMaxLength(32);

        builder.Property(x => x.RegistrationCertificate)
            .HasMaxLength(64);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.OrganizationClient)
            .WithMany(x => x.Vehicles)
            .HasForeignKey(x => x.OrganizationClientId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}


