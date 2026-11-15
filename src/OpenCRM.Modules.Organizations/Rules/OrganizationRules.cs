using System.ComponentModel.DataAnnotations;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Rules;

/// <summary> Правила и нормализация данных организаций. </summary>
internal static class OrganizationRules
{
    /// <summary> Возвращает название статуса приглашения по коду. </summary>
    internal static string GetInvitationStatusName(string statusCode)
    {
        return statusCode switch
        {
            OrganizationInvitationStatusCodes.Pending => "Ожидает ответа",
            OrganizationInvitationStatusCodes.Accepted => "Принято",
            OrganizationInvitationStatusCodes.Declined => "Отклонено",
            OrganizationInvitationStatusCodes.Revoked => "Отозвано",
            _ => statusCode
        };
    }

    /// <summary> Требует, чтобы пользователь был владельцем организации. </summary>
    internal static void EnsureOwner(string userId, Organization organization)
    {
        if (organization.OwnerUserId != userId)
        {
            throw new InvalidOperationException("Изменять критичные настройки может только владелец организации.");
        }
    }

    /// <summary> Формирует описание изменений имени и сценария организации. </summary>
    internal static string BuildOrganizationChangeDescription(
        string previousName,
        string currentName,
        string previousScenarioCode,
        string currentScenarioCode)
    {
        var changes = new List<string>();
        OrganizationRules.AddChange(changes, "Название", previousName, currentName);
        OrganizationRules.AddChange(changes, "Тип организации", previousScenarioCode, currentScenarioCode);
        return changes.Count == 0 ? "Изменены настройки организации." : string.Join("; ", changes);
    }

    /// <summary> Добавляет описание изменения поля, если значение изменилось. </summary>
    internal static void AddChange(List<string> changes, string label, string? previousValue, string? currentValue)
    {
        var previous = string.IsNullOrWhiteSpace(previousValue) ? "не указано" : previousValue.Trim();
        var current = string.IsNullOrWhiteSpace(currentValue) ? "не указано" : currentValue.Trim();
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            changes.Add($"{label}: {previous} -> {current}");
        }
    }

    /// <summary> Нормализует код роли. </summary>
    internal static string NormalizeRole(string? roleCode)
    {
        var normalized = OrganizationRules.NormalizeCode(roleCode);
        return OrganizationRoleCodes.All.Contains(normalized)
            ? normalized
            : OrganizationRoleCodes.Employee;
    }

    /// <summary> Нормализует код. </summary>
    internal static string NormalizeCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToLowerInvariant();
    }

    /// <summary> Нормализует код стиля документов. </summary>
    internal static string NormalizeDocumentStyle(string? styleCode)
    {
        var normalized = OrganizationRules.NormalizeOrganizationTypeAlias(OrganizationRules.NormalizeCode(styleCode));
        return normalized is "auto" or "detailing" or "fleet" or "minimal"
            ? normalized
            : "auto";
    }

    /// <summary> Нормализует режим рабочего процесса обслуживания. </summary>
    internal static string NormalizeMaintenanceWorkflowMode(string? mode)
    {
        var normalized = OrganizationRules.NormalizeCode(mode);
        return normalized is "simple" or "staged"
            ? normalized
            : "staged";
    }

    /// <summary> Приводит устаревший код типа организации к актуальному. </summary>
    internal static string NormalizeOrganizationTypeAlias(string code)
    {
        return OrganizationScenarioProcessCatalog.NormalizeScenarioCode(code);
    }

    /// <summary> Нормализует адрес email. </summary>
    internal static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    /// <summary> Нормализует обязательное значение или возвращает ошибку. </summary>
    internal static string NormalizeRequired(string? value, string error)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new InvalidOperationException(error)
            : normalized;
    }

    /// <summary> Нормализует необязательное значение. </summary>
    internal static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary> Проверяет формат необязательных российских реквизитов до изменения организации. </summary>
    internal static void ValidateRequisites(string? inn, string? kpp, string? ogrn)
    {
        var requisites = new UpdateOrganizationRequisitesDto
        {
            Inn = OrganizationRules.NormalizeNullable(inn),
            Kpp = OrganizationRules.NormalizeNullable(kpp),
            Ogrn = OrganizationRules.NormalizeNullable(ogrn)
        };

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(requisites, new ValidationContext(requisites), errors, true))
        {
            throw new InvalidOperationException(string.Join(" ", errors.Select(error => error.ErrorMessage)));
        }
    }

    /// <summary> Возвращает порядок сортировки роли. </summary>
    internal static int RoleSortOrder(string roleCode) => roleCode switch
    {
        OrganizationRoleCodes.Owner => 0,
        OrganizationRoleCodes.Admin => 1,
        OrganizationRoleCodes.Employee => 2,
        OrganizationRoleCodes.Performer => 3,
        OrganizationRoleCodes.Viewer => 4,
        _ => 10
    };

    /// <summary> Возвращает название роли по коду. </summary>
    internal static string GetRoleName(string roleCode) => roleCode switch
    {
        OrganizationRoleCodes.Owner => "Владелец",
        OrganizationRoleCodes.Admin => "Администратор",
        OrganizationRoleCodes.Employee => "Сотрудник",
        OrganizationRoleCodes.Performer => "Исполнитель",
        OrganizationRoleCodes.Viewer => "Только просмотр",
        _ => roleCode
    };
}
