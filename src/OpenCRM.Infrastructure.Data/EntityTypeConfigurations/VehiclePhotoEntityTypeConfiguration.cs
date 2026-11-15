using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности фотографии техники. </summary>
public class VehiclePhotoEntityTypeConfiguration : IEntityTypeConfiguration<VehiclePhoto>
{
    /// <summary> Настраивает отображение сущности фотографии техники. </summary>
    public void Configure(EntityTypeBuilder<VehiclePhoto> builder)
    {
        builder.ToTable("VehiclePhotos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(p => p.UserId)
            .IsRequired(false)
            .HasMaxLength(450);

        builder.Property(p => p.Path)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.Type)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(MediaType.VehiclePhoto);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.Size)
            .IsRequired();

        builder.Property(p => p.ContentType)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("image/jpeg");

        builder.Property(p => p.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.VehicleId)
            .IsRequired();

        builder.Property(p => p.AlbumId)
            .IsRequired(false);

        builder.Property(p => p.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(p => new { p.UserId, p.VehicleId })
            .HasDatabaseName("IX_VehiclePhotos_User_Vehicle");

        builder.HasIndex(p => new { p.VehicleId, p.AlbumId, p.IsDeleted })
            .HasDatabaseName("IX_VehiclePhotos_Vehicle_Album_Deleted");

        builder.HasIndex(p => new { p.UserId, p.IsDeleted })
            .HasDatabaseName("IX_VehiclePhotos_User_Deleted");

        builder.HasIndex(p => p.VehicleId)
            .HasDatabaseName("IX_VehiclePhotos_VehicleId");

        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("IX_VehiclePhotos_CreatedAt");

        builder.HasIndex(p => new { p.UserId, p.Type })
            .HasDatabaseName("IX_VehiclePhotos_User_Type");

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Vehicle)
            .WithMany(v => v.Photos)
            .HasForeignKey(p => p.VehicleId)
            .HasPrincipalKey(c => c.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Album)
            .WithMany(a => a.Photos)
            .HasForeignKey(p => p.AlbumId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
