namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт архивирования и восстановления организаций. </summary>
public interface IOrganizationArchiveService
{
    /// <summary> Переводит организацию в архив. </summary>
    Task ArchiveAsync(string currentUserId, Guid organizationId);

    /// <summary> Восстанавливает организацию из архива. </summary>
    Task RestoreAsync(string currentUserId, Guid organizationId);

    /// <summary> Архивирует любую организацию по решению администратора. </summary>
    Task ArchiveByAdminAsync(string adminUserId, Guid organizationId, string reason);

    /// <summary> Восстанавливает неудалённую организацию по решению администратора. </summary>
    Task RestoreByAdminAsync(string adminUserId, Guid organizationId, string reason);

    /// <summary> Выполняет очистку архивных организаций. </summary>
    Task CleanUpArchiveAsync();
}
