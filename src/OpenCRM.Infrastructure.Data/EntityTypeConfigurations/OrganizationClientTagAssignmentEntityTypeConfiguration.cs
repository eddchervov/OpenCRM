using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности привязки тега к клиенту организации. </summary>
public class OrganizationClientTagAssignmentEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationClientTagAssignment>
{
    /// <summary> Настраивает отображение сущности привязки тега к клиенту. </summary>
    public void Configure(EntityTypeBuilder<OrganizationClientTagAssignment> builder)
    {
        builder.HasKey(x => new { x.OrganizationClientId, x.OrganizationClientTagId });

        builder.HasIndex(x => x.OrganizationClientTagId);

        builder.HasOne(x => x.OrganizationClient)
            .WithMany(x => x.TagAssignments)
            .HasForeignKey(x => x.OrganizationClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.OrganizationClientTag)
            .WithMany(x => x.ClientAssignments)
            .HasForeignKey(x => x.OrganizationClientTagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
