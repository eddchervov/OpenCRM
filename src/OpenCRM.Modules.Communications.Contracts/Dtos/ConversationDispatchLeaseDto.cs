namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Wakeup-событие с временной арендой публикации, не меняющей статус сообщения. </summary>
public sealed record ConversationDispatchLeaseDto
{
    public Guid DispatchId { get; init; }
    public Guid LeaseId { get; init; }
    public Guid MessageId { get; init; }
    public Guid RequestId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public bool IsReplay { get; init; }
}
