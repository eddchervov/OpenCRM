namespace Payments.Service.Domain;

/// <summary> Сохраняет предметную идемпотентность команд после истечения транспортного inbox. </summary>
public sealed class CommandReceipt
{
    public string ApplicationId { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}
