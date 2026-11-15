namespace OpenCRM.Services.Interfaces.Media;

/// <summary>Проверка доступа к медиа в личном рабочем пространстве или организации.</summary>
public interface IMediaWorkspaceAccess
{
    /// <summary> Проверяет право пользователя на чтение медиа в рабочем пространстве. </summary>
    Task EnsureCanReadWorkspaceAsync(string userId, Guid? organizationId);

    /// <summary> Проверяет право пользователя на запись медиа в рабочем пространстве. </summary>
    Task EnsureCanWriteWorkspaceAsync(string userId, Guid? organizationId, string? permissionCode = null);
}