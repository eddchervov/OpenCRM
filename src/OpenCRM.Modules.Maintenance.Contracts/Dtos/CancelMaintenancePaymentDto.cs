namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для отмены платежа по обслуживанию. </summary>
public sealed class CancelMaintenancePaymentDto
{
    /// <summary> Причина отмены платежа. </summary>
    public string Reason { get; set; } = string.Empty;
}
