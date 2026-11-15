using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations;

/// <summary> Чтение данных сценариев организации. </summary>
internal sealed class OrganizationScenarioReader(OpenCRMContext context)
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Загружает названия сценариев по кодам. </summary>
    internal Task<Dictionary<string, string>> LoadNamesAsync()
    {
        return this._context.OrganizationTypeDefinitions
            .AsNoTracking()
            .Select(type => new
            {
                type.Code,
                Name = type.Code == OrganizationTypeCodes.ServiceStation ? "Автосервис" : type.Name
            })
            .ToDictionaryAsync(x => x.Code, x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary> Проверяет, что сценарий организации не изменился. </summary>
    internal Task EnsureUnchangedAsync(string currentScenarioCode, string requestedScenarioCode)
    {
        if (string.IsNullOrWhiteSpace(requestedScenarioCode))
        {
            return Task.CompletedTask;
        }

        var normalizedCurrentScenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(currentScenarioCode);
        var normalizedRequestedScenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(requestedScenarioCode);

        if (string.Equals(normalizedCurrentScenarioCode, normalizedRequestedScenarioCode, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException("Тип организации нельзя изменить после создания.");
    }

    /// <summary> Нормализует и проверяет код сценария. </summary>
    internal async Task<string> NormalizeAsync(string? scenarioCode)
    {
        var normalized = OrganizationRules.NormalizeOrganizationTypeAlias(OrganizationRules.NormalizeCode(scenarioCode));
        // HIDDEN_FLEET: запрет относится к созданию; чтение и EnsureUnchangedAsync сохраняют старые организации. См. docs/hidden-fleet-scenario.md.
        if (normalized == OrganizationTypeCodes.Fleet)
        {
            throw new InvalidOperationException("Создание организаций этого типа временно недоступно.");
        }

        var exists = await this._context.OrganizationTypeDefinitions
            .AnyAsync(x => x.Code == normalized && x.IsSupported && x.IsActive);

        return exists ? normalized : OrganizationTypeCodes.Detailing;
    }
}
