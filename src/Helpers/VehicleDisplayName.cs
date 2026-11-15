namespace Helpers;

/// <summary> Подпись автомобиля для интерфейса и документов, без изменения его данных. </summary>
public static class VehicleDisplayName
{
    /// <summary> Возвращает марку и модель, а для неизвестного автомобиля — подпись с госномером. </summary>
    public static string Format(string? brandName, string? modelName, string? registrationPlate)
    {
        var identity = string.Join(' ', new[] { brandName?.Trim(), modelName?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrEmpty(identity))
        {
            return identity;
        }

        return string.IsNullOrWhiteSpace(registrationPlate)
            ? "Автомобиль"
            : $"Автомобиль · {registrationPlate.Trim()}";
    }

    /// <summary> Возвращает дополнительные сведения техники без повторения номера в имени. </summary>
    public static string? FormatSubtitle(
        string? name,
        int? yearIssue,
        string? clientName,
        string? registrationPlate = null,
        bool includeVehicleName = true)
    {
        var parts = new List<string>();
        var normalizedName = includeVehicleName ? VehicleDisplayName.NormalizeSubtitleName(name, registrationPlate) : null;

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            parts.Add(normalizedName);
        }

        if (yearIssue.HasValue)
        {
            parts.Add($"{yearIssue.Value} г.");
        }

        if (!string.IsNullOrWhiteSpace(clientName))
        {
            parts.Add(clientName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(registrationPlate))
        {
            parts.Add(registrationPlate.Trim());
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    /// <summary> Нормализует подпись к названию техники, убирая дублирование госномера. </summary>
    private static string? NormalizeSubtitleName(string? name, string? registrationPlate)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var normalizedPlate = registrationPlate?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedPlate)
            && normalized.Equals(normalizedPlate, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }
}
