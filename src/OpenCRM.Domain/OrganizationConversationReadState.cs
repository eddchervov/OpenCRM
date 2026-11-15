namespace OpenCRM.Domain;

/// <summary> Позиция прочтения диалога отдельным сотрудником организации. </summary>
public sealed class OrganizationConversationReadState
{
    public Guid ConversationId { get; set; }
    public OrganizationConversation Conversation { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public long LastReadSequence { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
