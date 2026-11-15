using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности организации. </summary>
public class OrganizationEntityTypeConfiguration : IEntityTypeConfiguration<Organization>
{
    /// <summary> Настраивает отображение сущности организации. </summary>
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(x => x.VisitReminderChannelCode).HasMaxLength(16);
        builder.Property(x => x.VisitReminderTemplate).HasMaxLength(1000);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(600);

        builder.Property(x => x.LegalName)
            .HasMaxLength(240);

        builder.Property(x => x.Inn)
            .HasMaxLength(12);

        builder.Property(x => x.Kpp)
            .HasMaxLength(9);

        builder.Property(x => x.Ogrn)
            .HasMaxLength(15);

        builder.Property(x => x.LegalAddress)
            .HasMaxLength(400);

        builder.Property(x => x.ActualAddress)
            .HasMaxLength(400);

        builder.Property(x => x.Phone)
            .HasMaxLength(40);

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        builder.Property(x => x.SignatoryPosition)
            .HasMaxLength(120);

        builder.Property(x => x.SignatoryName)
            .HasMaxLength(160);

        builder.Property(x => x.SignatoryBasis)
            .HasMaxLength(160);

        builder.Property(x => x.DocumentStyleCode)
            .HasMaxLength(40)
            .HasDefaultValue("auto")
            .IsRequired();

        builder.Property(x => x.MaintenanceWorkflowMode)
            .HasMaxLength(40)
            .HasDefaultValue("staged")
            .IsRequired();

        builder.Property(x => x.PrimaryScenarioCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.WorkingDays)
            .HasDefaultValueSql("ARRAY[]::integer[]");

        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.PrimaryScenarioCode);
        builder.HasIndex(x => x.IsArchived);
        builder.HasIndex(x => x.IsDeleted);

        builder.HasOne(x => x.OwnerUser)
            .WithMany(x => x.OwnedOrganizations)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
