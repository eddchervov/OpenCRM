namespace OpenCRM.Modules.Notifications;

/// <summary> Возвращает актуальные защищённые учётные данные проверенного канала организации. </summary>
public interface IOrganizationChannelCredentialReader
{
    Task<OrganizationChannelCredential?> GetActiveCredentialAsync(
        Guid organizationId,
        Guid connectionId,
        CancellationToken cancellationToken = default);
}

/// <summary> Учётные данные актуального подключения внешнего канала. </summary>
public sealed record OrganizationChannelCredential(
    string ProviderCode,
    string ProviderBotId,
    string Token,
    string Username);
