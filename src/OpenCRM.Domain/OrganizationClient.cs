namespace OpenCRM.Domain;

public class OrganizationClient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? CompanyTypeCode { get; set; }
    public string? Phone { get; set; }
    public string PartyTypeCode { get; set; } = "person";
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? LegalAddress { get; set; }
    public string? Comment { get; set; }
    public string StatusCode { get; set; } = "active";
    public string? ResponsibleUserId { get; set; }
    public long? MaxUserId { get; set; }
    public string? MaxLinkTokenHash { get; set; }
    public DateTime? MaxLinkExpiresAt { get; set; }
    public DateTime? MaxConnectedAt { get; set; }
    public bool MaxNotificationsEnabled { get; set; }
    public long? TelegramChatId { get; set; }
    public string? TelegramLinkTokenHash { get; set; }
    public DateTime? TelegramLinkExpiresAt { get; set; }
    public DateTime? TelegramConnectedAt { get; set; }
    public bool TelegramNotificationsEnabled { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletionDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Транспортные средства клиента организации. </summary>
    public ICollection<Vehicle> Vehicles { get; set; } = new HashSet<Vehicle>();

    /// <summary> События истории по клиенту организации. </summary>
    public ICollection<OrganizationClientHistoryEvent> HistoryEvents { get; set; } = new HashSet<OrganizationClientHistoryEvent>();

    /// <summary> Закрепления тегов за клиентом организации. </summary>
    public ICollection<OrganizationClientTagAssignment> TagAssignments { get; set; } = new HashSet<OrganizationClientTagAssignment>();

    /// <summary> Помечает клиента организации удалённым с указанием даты. </summary>
    public void Delete(DateTime deletionDate)
    {
        IsDeleted = true;
        DeletionDate = deletionDate;
        UpdatedAt = deletionDate;
    }

    /// <summary> Восстанавливает ранее удалённого клиента организации. </summary>
    public void Restore(DateTime restoredAt)
    {
        IsDeleted = false;
        DeletionDate = null;
        UpdatedAt = restoredAt;
    }
}
