using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LegalName { get; set; }
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? Ogrn { get; set; }
    public string? LegalAddress { get; set; }
    public string? ActualAddress { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? SignatoryPosition { get; set; }
    public string? SignatoryName { get; set; }
    public string? SignatoryBasis { get; set; }
    public string DocumentStyleCode { get; set; } = "auto";
    public string MaintenanceWorkflowMode { get; set; } = "staged";
    public bool AllowWorkSelfAssignment { get; set; }
    public bool SetupAutoOpenPending { get; set; }
    public int SetupStep { get; set; }
    public int SetupRevision { get; set; }
    public DateTime? SetupCompletedAt { get; set; }
    public string? ShiftTimeZoneId { get; set; }

    public int? VisitReminderHoursBefore { get; set; }

    public string VisitReminderChannelCode { get; set; } = "auto";

    public string? VisitReminderTemplate { get; set; }
    public TimeOnly? WorkdayStart { get; set; }
    public TimeOnly? WorkdayEnd { get; set; }
    public int[] WorkingDays { get; set; } = [];
    public string PrimaryScenarioCode { get; set; } = OrganizationTypeCodes.Personal;
    public string OwnerUserId { get; set; } = string.Empty;
    public ApplicationUser OwnerUser { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletionDate { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchiveDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Время последнего запроса полной выгрузки данных организации в UTC. </summary>
    public DateTime? LastDataExportRequestedAt { get; set; }

    public ICollection<OrganizationMember> Members { get; set; } = new HashSet<OrganizationMember>();
    public ICollection<OrganizationInvitation> Invitations { get; set; } = new HashSet<OrganizationInvitation>();
    public ICollection<OrganizationRolePermissionOverride> RolePermissionOverrides { get; set; } = new HashSet<OrganizationRolePermissionOverride>();
    public ICollection<OrganizationAuditEvent> AuditEvents { get; set; } = new HashSet<OrganizationAuditEvent>();
    public ICollection<OrganizationClient> Clients { get; set; } = new HashSet<OrganizationClient>();
    public ICollection<VehicleDefect> Defects { get; set; } = new HashSet<VehicleDefect>();

    public void Archive(DateTime archiveDate)
    {
        IsArchived = true;
        ArchiveDate = archiveDate;
        IsActive = false;
        UpdatedAt = archiveDate;
    }

    public void Restore(DateTime restoreDate)
    {
        IsDeleted = false;
        DeletionDate = null;
        IsArchived = false;
        ArchiveDate = null;
        IsActive = true;
        UpdatedAt = restoreDate;
    }

    public void Delete(DateTime deletionDate)
    {
        IsDeleted = true;
        DeletionDate = deletionDate;
        IsArchived = false;
        IsActive = false;
        UpdatedAt = deletionDate;
    }
}
