namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Ограниченное преобразование значения исходного поля.</summary>
public class ImportFieldTransformation
{
    /// <summary>Тип преобразования: phone, date-exact, decimal или value-map.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Строгий формат даты из разрешённого списка.</summary>
    public string? Format { get; set; }

    /// <summary>Явные пары исходных и целевых значений.</summary>
    public Dictionary<string, string> Values { get; set; } = [];
}
