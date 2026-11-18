using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация обслуживания техники. </summary>
public class MaintenanceEntityTypeConfiguration : IEntityTypeConfiguration<Maintenance>
{
    /// <summary> Настраивает модель обслуживания техники. </summary>
    public void Configure(EntityTypeBuilder<Maintenance> builder)
    {
        builder.Property(maintenance => maintenance.CurrencyCode).HasMaxLength(3).HasDefaultValue("RUB").IsRequired();
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Maintenance_Handover",
            "(\"ReadyForHandoverAt\" IS NULL OR \"Status\" = 3) AND "
            + "(\"HandedOverAt\" IS NULL OR (\"ReadyForHandoverAt\" IS NOT NULL AND \"HandedOverAt\" >= \"ReadyForHandoverAt\"))"));

        builder.Property(x => x.Version).IsRowVersion();
        builder.Property(x => x.VisitReminderEnabled).HasDefaultValue(true);
        builder.HasIndex(x => new { x.PlannedDate, x.PlannedStartTime, x.Id })
            .HasDatabaseName("IX_Maintenance_VisitReminders")
            .HasFilter("\"Status\" = 1 AND NOT \"IsDeleted\" AND \"VisitReminderEnabled\" AND \"PlannedStartTime\" IS NOT NULL");

        builder.Property(x => x.Status)
            .HasDefaultValue(MaintenanceStatus.Completed);

        builder.Property(x => x.Location)
            .HasMaxLength(250);

        builder.HasOne<OrganizationEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Performer)
            .HasMaxLength(250);

        builder.Property(x => x.IntakeDiagramVersion).HasMaxLength(64);

        builder.Property(x => x.IntakeNotes)
            .HasMaxLength(4000);

        builder.Property(x => x.IntakeCompleteness)
            .HasMaxLength(4000);

        builder.Property(x => x.IntakeVisibleDamage)
            .HasMaxLength(4000);

        builder.Property(x => x.Comment)
            .HasMaxLength(4000);

        builder.Property(x => x.BuyerName).HasMaxLength(250);
        builder.Property(x => x.BuyerPartyTypeCode).HasMaxLength(16);
        builder.Property(x => x.BuyerInn).HasMaxLength(20);
        builder.Property(x => x.BuyerKpp).HasMaxLength(20);
        builder.Property(x => x.BuyerLegalAddress).HasMaxLength(500);
        builder.HasOne<OrganizationClient>().WithMany().HasForeignKey(x => x.BuyerClientId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.DiscountAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.PaidAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.MaterialCostAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.LaborCostAmount)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.WorkActOrganizationId, x.WorkActNumber })
            .IsUnique()
            .HasFilter("\"WorkActOrganizationId\" IS NOT NULL AND \"WorkActNumber\" IS NOT NULL");

        builder.HasMany(x => x.Details)
            .WithOne()
            .HasForeignKey(detail => detail.MaintenanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Details)
            .HasField("_details")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}

