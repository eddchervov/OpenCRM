using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Bookings;

/// <summary> Общая логика подготовки данных быстрой записи. </summary>
internal sealed class QuickBookingSupport(
    IQuickBookingClientLookupService clientLookup,
    IOrganizationClientCommandService organizationClientService,
    ICatalogQueryService scenarioCatalogService)
{
    /// <summary> Зависимость поиска клиента быстрой записи. </summary>
    private readonly IQuickBookingClientLookupService _clientLookup = clientLookup;

    /// <summary> Зависимость команд работы с клиентами организации. </summary>
    private readonly IOrganizationClientCommandService _organizationClientService = organizationClientService;

    /// <summary> Зависимость запросов каталога сценариев. </summary>
    private readonly ICatalogQueryService _scenarioCatalogService = scenarioCatalogService;

    /// <summary> Подготавливает услуги быстрой записи, дополняя из каталога. </summary>
    internal async Task PrepareServicesAsync(
        string userId,
        Guid organizationId,
        List<CreateMaintenanceDetailDto> services)
    {
        var hasDuplicateServices = services
            .Where(item => item.CatalogItemId.HasValue)
            .GroupBy(item => item.CatalogItemId)
            .Any(group => group.Count() > 1);

        if (services.Count == 0 || services.Count > 100 || hasDuplicateServices)
        {
            throw new InvalidOperationException("Добавьте от 1 до 100 работ. Для повторяющейся услуги из справочника измените количество.");
        }

        foreach (var detail in services)
        {
            if (detail.MaterialCatalogItemId.HasValue || detail.MaterialQuantity.HasValue || !string.IsNullOrWhiteSpace(detail.MaterialName))
            {
                throw new InvalidOperationException("Материалы добавляются в карточке записи отдельно от услуг.");
            }

            if (!detail.CatalogItemId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(detail.Name))
                {
                    throw new InvalidOperationException("Укажите название работы.");
                }

                detail.Name = detail.Name.Trim();
                continue;
            }

            var service = await this._scenarioCatalogService.GetActiveServiceAsync(userId, organizationId, detail.CatalogItemId.Value);
            detail.Name = service.Name;
        }
    }

    /// <summary> Находит или создаёт клиента и возвращает его идентификатор. </summary>
    internal async Task<(Guid? ClientId, bool Created)> ResolveClientIdAsync(
        string userId,
        Guid organizationId,
        Guid? requestedClientId,
        string? clientPhone,
        string? clientName,
        string clientCreatedComment)
    {
        if (requestedClientId.HasValue)
        {
            var exists = await this._clientLookup.ExistsAsync(userId, organizationId, requestedClientId.Value);

            if (!exists)
            {
                throw new InvalidOperationException("Выбранный клиент не найден.");
            }

            return (requestedClientId.Value, false);
        }

        return await this._organizationClientService.ResolveQuickBookingClientAsync(
            userId, organizationId, clientPhone, clientName, clientCreatedComment);
    }

    /// <summary> Формирует список услуг с учётом выбранной услуги и цены. </summary>
    internal static List<CreateMaintenanceDetailDto> CreateServices(
        List<CreateMaintenanceDetailDto>? services,
        Guid? serviceId,
        decimal? price)
    {
        return services ??
        [
            new CreateMaintenanceDetailDto
            {
                Name = string.Empty,
                CatalogItemId = serviceId,
                Description = null,
                Price = price.HasValue ? (decimal)price.Value : null
            }
        ];
    }

    /// <summary> Нормализует идентификатор модели автомобиля. </summary>
    internal static int? NormalizeModelId(int? modelId) => modelId is > 0 ? modelId : null;

    /// <summary> Нормализует строку для отображения. </summary>
    internal static string NormalizeDisplay(string? value) =>
        QuickBookingSupport.NormalizeNullable(value)?.ToUpperInvariant() ?? string.Empty;

    /// <summary> Нормализует необязательную строку. </summary>
    internal static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
