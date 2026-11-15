using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class AssistantEntityTypeConfiguration :
    IEntityTypeConfiguration<AssistantSettings>,
    IEntityTypeConfiguration<AssistantOrganizationAccess>,
    IEntityTypeConfiguration<AssistantConversation>,
    IEntityTypeConfiguration<AssistantMessage>,
    IEntityTypeConfiguration<AssistantDraft>,
    IEntityTypeConfiguration<AssistantUsage>,
    IEntityTypeConfiguration<AssistantTelegramBinding>,
    IEntityTypeConfiguration<AssistantTelegramLink>,
    IEntityTypeConfiguration<AssistantTelegramUpdate>,
    IEntityTypeConfiguration<AssistantAudit>
{
    public void Configure(EntityTypeBuilder<AssistantSettings> builder)
    {
        builder.ToTable("AssistantSettings", table => table.HasCheckConstraint("CK_AssistantSettings_Singleton", "\"Id\" = 1"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TextModel).HasMaxLength(120);
        builder.Property(item => item.SpeechModel).HasMaxLength(120);
        builder.Property(item => item.VoiceModel).HasMaxLength(120);
        builder.Property(item => item.VoiceName).HasMaxLength(120);
        builder.Property(item => item.DailyBudgetRub).HasPrecision(18, 8);
        builder.Property(item => item.MonthlyBudgetRub).HasPrecision(18, 8);
        builder.Property(item => item.TextReservationRub).HasPrecision(18, 8);
        builder.Property(item => item.AudioReservationRub).HasPrecision(18, 8);
        builder.Property(item => item.Revision).IsConcurrencyToken();
    }

    public void Configure(EntityTypeBuilder<AssistantOrganizationAccess> builder)
    {
        builder.ToTable("AssistantOrganizationAccess");
        builder.HasKey(item => item.OrganizationId);
        builder.HasOne(item => item.Organization).WithOne().HasForeignKey<AssistantOrganizationAccess>(item => item.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(item => item.UpdatedByUserId).HasMaxLength(450);
        builder.Property(item => item.DailyBudgetRub).HasPrecision(18, 8);
        builder.Property(item => item.MonthlyBudgetRub).HasPrecision(18, 8);
    }

    public void Configure(EntityTypeBuilder<AssistantConversation> builder)
    {
        builder.ToTable("AssistantConversations");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.Property(item => item.Channel).HasMaxLength(20);
        builder.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.UserId, item.OrganizationId, item.Channel, item.ExpiresAt });
        builder.Property(item => item.EngineStateJson).HasColumnType("jsonb");
        builder.Property(item => item.SelectionJson).HasColumnType("jsonb");
    }

    public void Configure(EntityTypeBuilder<AssistantMessage> builder)
    {
        builder.ToTable("AssistantMessages");
        builder.HasKey(item => item.Id);
        builder.HasOne(item => item.Conversation).WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.ConversationId, item.Sequence }).IsUnique();
        builder.Property(item => item.Role).HasMaxLength(20);
        builder.Property(item => item.Text).HasMaxLength(4000);
    }

    public void Configure(EntityTypeBuilder<AssistantDraft> builder)
    {
        builder.ToTable("AssistantDrafts");
        builder.HasKey(item => item.Id);
        builder.HasOne(item => item.Conversation).WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.Property(item => item.Channel).HasMaxLength(20);
        builder.Property(item => item.Action).HasMaxLength(80);
        builder.Property(item => item.Status).HasMaxLength(30);
        builder.Property(item => item.CommandJson).HasColumnType("jsonb");
        builder.Property(item => item.SelectionJson).HasColumnType("jsonb");
        builder.Property(item => item.PreviewJson).HasColumnType("jsonb");
        builder.Property(item => item.ResultJson).HasColumnType("jsonb");
        builder.Property(item => item.Snapshot).HasMaxLength(128);
        builder.Property(item => item.FailureMessage).HasMaxLength(1000);
        builder.HasIndex(item => new { item.ConversationId, item.Status });
    }

    public void Configure(EntityTypeBuilder<AssistantUsage> builder)
    {
        builder.ToTable("AssistantUsage");
        builder.HasKey(item => item.Id);
        builder.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AssistantConversation>().WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.Property(item => item.Channel).HasMaxLength(20);
        builder.Property(item => item.Kind).HasMaxLength(30);
        builder.Property(item => item.Model).HasMaxLength(120);
        builder.Property(item => item.Status).HasMaxLength(30);
        builder.Property(item => item.ProviderRequestId).HasMaxLength(200);
        builder.Property(item => item.ErrorCode).HasMaxLength(100);
        builder.Property(item => item.CostRub).HasPrecision(18, 8);
        builder.Property(item => item.ReservedCostRub).HasPrecision(18, 8);
        builder.Property(item => item.AudioSeconds).HasPrecision(12, 3);
        builder.HasIndex(item => new { item.OrganizationId, item.CreatedAt });
        builder.HasIndex(item => item.CreatedAt);
        builder.HasIndex(item => new { item.Model, item.ProviderRequestId });
    }

    public void Configure(EntityTypeBuilder<AssistantTelegramBinding> builder)
    {
        builder.ToTable("AssistantTelegramBindings");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(item => item.SelectedOrganizationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AssistantConversation>().WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(item => item.TelegramUserId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        builder.HasIndex(item => item.UserId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
    }

    public void Configure(EntityTypeBuilder<AssistantTelegramLink> builder)
    {
        builder.ToTable("AssistantTelegramLinks");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.Property(item => item.TokenHash).HasMaxLength(64);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.TokenHash).IsUnique();
    }

    public void Configure(EntityTypeBuilder<AssistantTelegramUpdate> builder)
    {
        builder.ToTable("AssistantTelegramUpdates");
        builder.HasKey(item => item.UpdateId);
        builder.Property(item => item.UpdateId).ValueGeneratedNever();
        builder.Property(item => item.Status).HasMaxLength(30);
        builder.Property(item => item.PayloadJson).HasColumnType("jsonb");
        builder.Property(item => item.ResponseJson).HasColumnType("jsonb");
        builder.Property(item => item.ErrorCode).HasMaxLength(100);
        builder.HasIndex(item => new { item.Status, item.CreatedAt });
    }

    public void Configure(EntityTypeBuilder<AssistantAudit> builder)
    {
        builder.ToTable("AssistantAudits");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AdminUserId).HasMaxLength(450);
        builder.Property(item => item.Action).HasMaxLength(80);
        builder.Property(item => item.Description).HasMaxLength(2000);
        builder.HasIndex(item => item.CreatedAt);
    }
}
