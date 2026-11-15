namespace OpenCRM.Domain;

/// <summary> Проверенная связь клиента с внешней учётной записью в конкретном канале. </summary>
public sealed class OrganizationClientChannelLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ClientId { get; set; }
    public OrganizationClient Client { get; set; } = null!;
    public Guid ConnectionId { get; set; }
    public OrganizationChannelConnection Connection { get; set; } = null!;
    public string ExternalUserId { get; set; } = string.Empty;
    public string ExternalConversationId { get; set; } = string.Empty;
    public string VerificationSource { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? RevokedAt { get; set; }
}
