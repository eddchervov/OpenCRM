using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Загружает настройки помощника и доступ организаций. </summary>
    public Task<AdminAssistantOverview> GetAssistantAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var query = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search.Trim())}";
        }

        return this.GetAuthorizedReportAsync<AdminAssistantOverview>(
            $"api/v1/admin/assistant?{query}",
            "Не удалось загрузить настройки ИИ-помощника.",
            cancellationToken);
    }

    /// <summary> Сохраняет общие настройки помощника. </summary>
    public Task<AdminAssistantSettings> SaveAssistantSettingsAsync(
        AdminAssistantSettingsRequest settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return this.SendAuthorizedRequestAsync<AdminAssistantSettings>(
            HttpMethod.Put,
            "api/v1/admin/assistant/settings",
            settings,
            "Не удалось сохранить настройки ИИ-помощника.",
            cancellationToken);
    }

    /// <summary> Сохраняет доступ помощника для организации. </summary>
    public Task<AdminEmptyResponse> SaveAssistantOrganizationAccessAsync(
        Guid organizationId,
        AdminAssistantOrganizationAccessRequest access,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(access);

        return this.SendAuthorizedRequestAsync<AdminEmptyResponse>(
            HttpMethod.Put,
            $"api/v1/admin/assistant/organizations/{organizationId:D}/access",
            access,
            "Не удалось сохранить доступ ИИ-помощника для организации.",
            cancellationToken);
    }

    /// <summary> Загружает расходы помощника за период. </summary>
    public Task<AdminAssistantUsageResponse> GetAssistantUsageAsync(
        DateOnly from,
        DateOnly to,
        Guid? organizationId = null,
        string? channel = null,
        string? model = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var query = $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&page={page}&pageSize={pageSize}";
        if (organizationId is { } id && id != Guid.Empty)
        {
            query += $"&organizationId={id:D}";
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            query += $"&channel={Uri.EscapeDataString(channel.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            query += $"&model={Uri.EscapeDataString(model.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query += $"&status={Uri.EscapeDataString(status.Trim())}";
        }

        return this.GetAuthorizedReportAsync<AdminAssistantUsageResponse>(
            $"api/v1/admin/assistant/usage?{query}",
            "Не удалось загрузить расходы ИИ-помощника.",
            cancellationToken);
    }

    /// <summary> Сверяет стоимость запроса с неизвестными расходами. </summary>
    public Task<AdminEmptyResponse> SettleAssistantUsageAsync(
        Guid usageId,
        decimal costRub,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(usageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(costRub);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminEmptyResponse>(
            HttpMethod.Post,
            $"api/v1/admin/assistant/usage/{usageId:D}/settle",
            new AdminAssistantUsageSettlementRequest { CostRub = costRub, Reason = reason.Trim() },
            "Не удалось сверить расходы ИИ-помощника.",
            cancellationToken);
    }
}
