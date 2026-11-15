using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Хранение очереди и готовых архивов отдельно от рабочих данных организации. </summary>
public sealed class OrganizationDataExportJobEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationDataExportJob>
{
    public void Configure(EntityTypeBuilder<OrganizationDataExportJob> builder)
    {
        builder.ToTable("OrganizationDataExportJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(job => job.State).HasMaxLength(20).IsRequired();
        builder.Property(job => job.StoragePath).HasMaxLength(500);
        builder.Property(job => job.ErrorMessage).HasMaxLength(1000);
        builder.HasOne<Organization>().WithMany().HasForeignKey(job => job.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(job => new { job.OrganizationId, job.RequestedAtUtc });
        builder.HasIndex(job => new { job.State, job.RequestedAtUtc });
        builder.HasIndex(job => job.CleanupAfterUtc);
        builder.HasIndex(job => job.OrganizationId).IsUnique().HasFilter("\"State\" IN ('queued', 'running')");
    }
}
