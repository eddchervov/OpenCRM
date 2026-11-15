using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Событие истории ручного обращения. </summary>
public class OrganizationInquiryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationInquiryId { get; set; }
    public OrganizationInquiry OrganizationInquiry { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public ApplicationUser ActorUser { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
