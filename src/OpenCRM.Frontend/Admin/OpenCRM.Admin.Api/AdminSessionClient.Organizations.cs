using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Архивирует организацию любого владельца с записью причины в аудит. </summary>
    public Task<AdminOperationResponse> ArchiveOrganizationAsync(
        Guid organizationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/organizations/{organizationId:D}/archive",
            new { reason = reason.Trim() },
            "Не удалось архивировать организацию.",
            cancellationToken);
    }

    /// <summary> Восстанавливает организацию любого владельца с записью причины в аудит. </summary>
    public Task<AdminOperationResponse> RestoreOrganizationAsync(
        Guid organizationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/organizations/{organizationId:D}/restore",
            new { reason = reason.Trim() },
            "Не удалось восстановить организацию.",
            cancellationToken);
    }

    /// <summary> Загружает страницу организаций с поиском, сортировкой и пагинацией. </summary>
    public Task<AdminOrganizationsPage> GetOrganizationsAsync(
        string? search = null,
        string? user = null,
        string sort = "created",
        bool descending = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sort);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var query = $"sort={Uri.EscapeDataString(sort)}&descending={descending.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            query += $"&user={Uri.EscapeDataString(user.Trim())}";
        }

        return this.GetAuthorizedReportAsync<AdminOrganizationsPage>(
            $"api/v1/admin/organizations?{query}",
            "Не удалось загрузить организации.",
            cancellationToken);
    }

    /// <summary> Загружает карточку организации. </summary>
    public Task<AdminOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);

        return this.GetAuthorizedReportAsync<AdminOrganization>(
            $"api/v1/admin/organizations/{organizationId:D}",
            "Не удалось загрузить организацию.",
            cancellationToken);
    }
}
