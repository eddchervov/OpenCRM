using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationClientChannelLinkEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationClientChannelLink>
{
    public void Configure(EntityTypeBuilder<OrganizationClientChannelLink> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.ExternalUserId).HasMaxLength(128).IsRequired();
        builder.Property(item => item.ExternalConversationId).HasMaxLength(128).IsRequired();
        builder.Property(item => item.VerificationSource).HasMaxLength(32).IsRequired();
        builder.HasIndex(item => new { item.ConnectionId, item.ExternalConversationId })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(item => new { item.ConnectionId, item.ExternalUserId })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(item => new { item.OrganizationId, item.ClientId, item.IsActive });
        builder.HasOne(item => item.Client)
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, item.ClientId })
            .HasPrincipalKey(client => new { client.OrganizationId, client.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Connection)
            .WithMany(connection => connection.ClientLinks)
            .HasForeignKey(item => new { item.OrganizationId, item.ConnectionId })
            .HasPrincipalKey(connection => new { connection.OrganizationId, connection.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
