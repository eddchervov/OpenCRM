using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности фотографии обслуживания. </summary>
public class MaintenancePhotoEntityTypeConfiguration : IEntityTypeConfiguration<MaintenancePhoto>
{
    /// <summary> Настраивает отображение сущности фотографии обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenancePhoto> builder)
    {
        builder.ToTable("MaintenancePhotos");

        // Первичный ключ
        builder.HasKey(p => p.Id);
        builder.HasAlternateKey(photo => new { photo.MaintenanceId, photo.Id });

        // Свойства
        builder.Property(p => p.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(p => p.UserId)
            .IsRequired(false) // Nullable для системных файлов
            .HasMaxLength(450);

        builder.Property(p => p.Path)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.Type)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(MediaType.MaintenancePhoto);

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

        builder.Property(p => p.MaintenanceId)
            .IsRequired();

        builder.Property(p => p.VehicleId)
            .IsRequired();

        // Индексы для оптимизации запросов
        builder.HasIndex(p => new { p.UserId, p.MaintenanceId, p.VehicleId })
            .HasDatabaseName("IX_MaintenancePhotos_User_Maintenance_Vehicle");

        builder.HasIndex(p => new { p.UserId, p.IsDeleted })
            .HasDatabaseName("IX_MaintenancePhotos_User_Deleted");

        builder.HasIndex(p => p.MaintenanceId)
            .HasDatabaseName("IX_MaintenancePhotos_MaintenanceId");

        builder.HasIndex(p => new { p.MaintenanceId, p.DetailId });

        builder.HasIndex(p => p.VehicleId)
            .HasDatabaseName("IX_MaintenancePhotos_VehicleId");

        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("IX_MaintenancePhotos_CreatedAt");

        builder.HasIndex(p => new { p.UserId, p.Type })
            .HasDatabaseName("IX_MaintenancePhotos_User_Type");

        // Внешние ключи и навигационные свойства
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull); // SET NULL для сохранения файлов при удалении пользователя

        builder.HasOne(p => p.Maintenance)
            .WithMany()
            .HasForeignKey(p => p.MaintenanceId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Vehicle)
            .WithMany()
            .HasForeignKey(p => p.VehicleId)
            .HasPrincipalKey(c => c.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


