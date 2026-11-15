using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Ограничивает связи повреждений фотографиями того же заказа. </summary>
public sealed class MaintenanceIntakeDamagePhotoEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceIntakeDamagePhoto>
{
    public void Configure(EntityTypeBuilder<MaintenanceIntakeDamagePhoto> builder)
    {
        builder.ToTable("MaintenanceIntakeDamagePhotos");
        builder.HasKey(link => new { link.DamageId, link.PhotoId });
        builder.HasOne(link => link.Damage).WithMany(damage => damage.Photos)
            .HasForeignKey(link => new { link.MaintenanceId, link.DamageId })
            .HasPrincipalKey(damage => new { damage.MaintenanceId, damage.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Photo).WithMany()
            .HasForeignKey(link => new { link.MaintenanceId, link.PhotoId })
            .HasPrincipalKey(photo => new { photo.MaintenanceId, photo.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
