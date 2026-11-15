using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности фотоальбома техники. </summary>
public class VehiclePhotoAlbumEntityTypeConfiguration : IEntityTypeConfiguration<VehiclePhotoAlbum>
{
    /// <summary> Настраивает отображение сущности фотоальбома техники. </summary>
    public void Configure(EntityTypeBuilder<VehiclePhotoAlbum> builder)
    {
        builder.ToTable("VehiclePhotoAlbums");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.VehicleId)
            .IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.UserId, x.VehicleId, x.IsDeleted })
            .HasDatabaseName("IX_VehiclePhotoAlbums_User_Vehicle_Deleted");

        builder.HasOne(x => x.Vehicle)
            .WithMany(x => x.PhotoAlbums)
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
