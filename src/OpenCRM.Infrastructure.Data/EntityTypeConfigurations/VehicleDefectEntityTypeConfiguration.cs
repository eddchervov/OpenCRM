using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности дефекта техники. </summary>
public class VehicleDefectEntityTypeConfiguration : IEntityTypeConfiguration<VehicleDefect>
{
    /// <summary> Настраивает отображение сущности дефекта техники. </summary>
    public void Configure(EntityTypeBuilder<VehicleDefect> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Title)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(1500);

        builder.Property(x => x.SeverityCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.StatusCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.VehicleId);
        builder.HasIndex(x => x.StatusCode);
        builder.HasIndex(x => new { x.OrganizationId, x.StatusCode, x.IsDeleted });

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Defects)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Vehicle)
            .WithMany(x => x.Defects)
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
