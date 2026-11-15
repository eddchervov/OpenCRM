using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Rules;

/// <summary> Правила нормализации и проверки данных клиентов. </summary>
internal static class ClientRules
{
    /// <summary> Нормализует обязательное значение или бросает ошибку. </summary>
    internal static string NormalizeRequired(string? value, string errorMessage)
    {
        var normalized = ClientRules.NormalizeNullable(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new InvalidOperationException(errorMessage)
            : normalized;
    }

    /// <summary> Нормализует необязательное строковое значение. </summary>
    internal static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary> Объединяет прежние заметки с комментарием без повторения одинакового текста. </summary>
    internal static string? NormalizeComment(string? comment, string? legacyNotes)
    {
        comment = ClientRules.NormalizeNullable(comment);
        legacyNotes = ClientRules.NormalizeNullable(legacyNotes);
        var mergedComment = comment;
        if (comment == null)
        {
            mergedComment = legacyNotes;
        }
        else if (legacyNotes != null && !string.Equals(comment, legacyNotes, StringComparison.Ordinal))
        {
            mergedComment = $"{comment}\n\n{legacyNotes}";
        }

        if (mergedComment?.Length > 4000)
        {
            throw new InvalidOperationException("Комментарий не должен превышать 4000 символов.");
        }

        return mergedComment;
    }

    /// <summary> Нормализует код типа контрагента. </summary>
    internal static string NormalizePartyTypeCode(string? value)
    {
        var normalized = ClientRules.NormalizeNullable(value);
        if (normalized is not ("person" or "company"))
        {
            throw new InvalidOperationException("Выберите корректный тип клиента.");
        }

        return normalized;
    }

    /// <summary> Нормализует необязательное учётное поле с ограничением длины. </summary>
    internal static string? NormalizeAccountingField(string? value, int maxLength, string fieldName)
    {
        var normalized = ClientRules.NormalizeNullable(value);
        if (normalized?.Length > maxLength)
        {
            throw new InvalidOperationException($"Поле «{fieldName}» не должно превышать {maxLength} символов.");
        }

        return normalized;
    }

    /// <summary> Проверяет составные имена, сохраняя совместимость с прежним единым наименованием. </summary>
    internal static (string Name, string? LastName, string? FirstName, string? MiddleName) NormalizeIdentity(
        string? name, string partyTypeCode, string? lastName, string? firstName, string? middleName)
    {
        if (partyTypeCode == "company")
        {
            return (ClientRules.NormalizeName(name), null, null, null);
        }

        lastName = ClientRules.NormalizeAccountingField(lastName, 60, "Фамилия");
        firstName = ClientRules.NormalizeAccountingField(firstName, 60, "Имя");
        middleName = ClientRules.NormalizeAccountingField(middleName, 60, "Отчество");
        if (lastName == null && firstName == null && middleName == null)
        {
            return (ClientRules.NormalizeName(name), null, null, null);
        }

        if (firstName == null)
        {
            throw new InvalidOperationException("Укажите имя клиента.");
        }

        var fullName = string.Join(" ", new[] { lastName, firstName, middleName }.Where(part => part != null));

        return (ClientRules.NormalizeName(fullName), lastName, firstName, middleName);
    }

    /// <summary> Проверяет ограничение отображаемого имени в том числе для прежних клиентов API. </summary>
    private static string NormalizeName(string? name)
    {
        var normalized = ClientRules.NormalizeRequired(name, "Укажите имя или название клиента.");
        if (normalized.Length > 160)
        {
            throw new InvalidOperationException("Имя или название клиента не должно превышать 160 символов.");
        }

        return normalized;
    }

    /// <summary> Проверяет явно выбранный вид организации, сохраняя прежний контракт без вида. </summary>
    internal static string? NormalizeCompanyType(string? value, string partyTypeCode)
    {
        if (partyTypeCode != "company")
        {
            return null;
        }

        var normalized = ClientRules.NormalizeNullable(value);
        if (normalized != null && normalized is not ("legal_entity" or "entrepreneur"))
        {
            throw new InvalidOperationException("Выберите юридическое лицо или ИП.");
        }

        return normalized;
    }

    /// <summary> Проверяет длину и формат ИНН согласно выбранному виду организации. </summary>
    internal static string? NormalizeInn(string? value, string? companyTypeCode)
    {
        if (companyTypeCode == null)
        {
            return ClientRules.NormalizeAccountingField(value, 20, "ИНН");
        }

        var normalized = ClientRules.NormalizeTaxCode(value);
        var length = companyTypeCode == "entrepreneur" ? 12 : 10;
        if (normalized != null && (normalized.Length != length || normalized.Any(character => character is < '0' or > '9')))
        {
            throw new InvalidOperationException($"ИНН должен содержать {length} цифр.");
        }

        return normalized;
    }

    /// <summary> Проверяет КПП юрлица; у ИП КПП отсутствует. </summary>
    internal static string? NormalizeKpp(string? value, string? companyTypeCode)
    {
        if (companyTypeCode == null)
        {
            return ClientRules.NormalizeAccountingField(value, 20, "КПП");
        }

        var normalized = ClientRules.NormalizeTaxCode(value)?.ToUpperInvariant();
        if (companyTypeCode == "entrepreneur")
        {
            if (normalized != null)
            {
                throw new InvalidOperationException("У ИП нет КПП. Очистите поле КПП.");
            }

            return null;
        }

        if (normalized != null && !System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[0-9]{4}[0-9A-Z]{2}[0-9]{3}$"))
        {
            throw new InvalidOperationException("КПП должен содержать 9 знаков: цифры, в 5–6-й позиции допустимы латинские буквы.");
        }

        return normalized;
    }

    /// <summary> Убирает пробелы при вставке реквизитов, не изменяя ведущие нули. </summary>
    private static string? NormalizeTaxCode(string? value)
    {
        return ClientRules.NormalizeNullable(value == null ? null : string.Concat(value.Where(character => !char.IsWhiteSpace(character))));
    }

    /// <summary> Нормализует дату записи истории, введённой вручную. </summary>
    internal static DateTime NormalizeManualHistoryDate(DateTime? occurredAt)
    {
        if (!occurredAt.HasValue)
        {
            return DateTime.UtcNow;
        }

        var value = occurredAt.Value;
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary> Нормализует список названий тегов. </summary>
    internal static IReadOnlyList<(string Name, string NormalizedName)> NormalizeTagNames(IEnumerable<string> tagNames)
    {
        return tagNames
            .SelectMany(x => (x ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Length > 48 ? x[..48] : x)
            .Select(x => (Name: x, NormalizedName: x.ToUpperInvariant()))
            .GroupBy(x => x.NormalizedName, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ToList();
    }

    /// <summary> Добавляет описание изменения поля в список изменений. </summary>
    internal static void AddChange(List<string> changes, string fieldName, string? previousValue, string? currentValue)
    {
        var previous = string.IsNullOrWhiteSpace(previousValue) ? "не указано" : previousValue.Trim();
        var current = string.IsNullOrWhiteSpace(currentValue) ? "не указано" : currentValue.Trim();
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            changes.Add($"{fieldName}: {previous} -> {current}");
        }
    }

    /// <summary> Строит отображаемое название техники. </summary>
    internal static string BuildVehicleDisplayName(string brandName, string modelName, int? yearIssue, string? name, string? registrationPlate = null)
    {
        var headline = Helpers.VehicleDisplayName.Format(brandName, modelName, registrationPlate);
        var parts = new List<string> { headline };

        if (yearIssue.HasValue)
        {
            parts.Add($"{yearIssue.Value} г.");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name.Trim());
        }

        if (!string.IsNullOrWhiteSpace(registrationPlate) && !headline.EndsWith(registrationPlate.Trim(), StringComparison.Ordinal))
        {
            parts.Add(registrationPlate.Trim());
        }

        return string.Join(" • ", parts);
    }

    /// <summary> Определяет отображаемое имя участника действий. </summary>
    internal static string? ResolveActorName(string? actorDisplayName, string? email, string? userName, string? originalEmail, bool isDeleted)
    {
        if (!string.IsNullOrWhiteSpace(actorDisplayName))
        {
            return actorDisplayName;
        }

        var fallback = !string.IsNullOrWhiteSpace(email)
            ? email
            : !string.IsNullOrWhiteSpace(userName)
                ? userName
                : originalEmail;

        if (string.IsNullOrWhiteSpace(fallback))
        {
            return isDeleted ? "Удаленный пользователь" : null;
        }

        return isDeleted ? $"{fallback} (удаленный пользователь)" : fallback;
    }

    /// <summary> Определяет отображаемое имя ответственного пользователя. </summary>
    internal static string? ResolveResponsibleDisplayName(string? responsibleUserId, IReadOnlyDictionary<string, string> responsibleDisplayNames)
    {
        return !string.IsNullOrWhiteSpace(responsibleUserId) && responsibleDisplayNames.TryGetValue(responsibleUserId, out var displayName)
            ? displayName
            : null;
    }

    /// <summary> Формирует описание записи истории. </summary>
    internal static string BuildHistoryDescription(string subject, string? details)
    {
        return string.IsNullOrWhiteSpace(details)
            ? subject
            : $"{subject}: {details.Trim()}";
    }

    /// <summary> Определяет дату записи истории по обслуживанию. </summary>
    internal static DateTime ResolveMaintenanceHistoryDate(OrganizationClientMaintenanceItemDto item)
    {
        return item.Status == MaintenanceStatus.Completed
            ? item.CompletedDate ?? item.Date
            : item.PlannedDate ?? item.Date;
    }

    /// <summary> Возвращает название типа обслуживания. </summary>
    internal static string MaintenanceTypeName(MaintenanceType type)
    {
        return type switch
        {
            MaintenanceType.Maintenance => "Обслуживание",
            MaintenanceType.Repair => "Ремонт",
            MaintenanceType.Refill => "Заправка",
            _ => "Другое"
        };
    }

    /// <summary> Возвращает название статуса обслуживания. </summary>
    internal static string MaintenanceStatusName(MaintenanceStatus status)
    {
        return status switch
        {
            MaintenanceStatus.Planned => "Запланировано",
            MaintenanceStatus.InProgress => "В работе",
            MaintenanceStatus.Completed => "Выполнено",
            MaintenanceStatus.Cancelled => "Отменено",
            _ => "Неизвестно"
        };
    }
}
