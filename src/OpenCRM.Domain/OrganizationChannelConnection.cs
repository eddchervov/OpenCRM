namespace OpenCRM.Domain;

/// <summary> Проверенное подключение организации к внешнему каналу. </summary>
public sealed class OrganizationChannelConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string ProviderCode { get; set; } = string.Empty;
    public string ImmutableProviderIdentity { get; set; } = string.Empty;
    public string? Username { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime VerifiedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<OrganizationConversation> Conversations { get; set; } = new HashSet<OrganizationConversation>();
    public ICollection<OrganizationClientChannelLink> ClientLinks { get; set; } = new HashSet<OrganizationClientChannelLink>();
}
