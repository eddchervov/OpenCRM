namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Повторный запрос выгрузки до окончания суточного ограничения. </summary>
public sealed class OrganizationDataExportLimitException(DateTime retryAtUtc)
    : Exception("Выгрузку данных организации можно запрашивать не чаще одного раза в 24 часа.")
{
    /// <summary> Момент следующего разрешённого запроса в UTC. </summary>
    public DateTime RetryAtUtc { get; } = retryAtUtc;
}
