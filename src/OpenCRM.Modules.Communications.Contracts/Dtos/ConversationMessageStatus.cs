namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Состояние доставки исходящего сообщения. </summary>
public enum ConversationMessageStatus
{
    Queued,
    Sending,
    Accepted,
    Failed,
    Uncertain,
    Cancelled
}
