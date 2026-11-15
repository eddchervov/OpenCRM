namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Существующий клиент, предложенный для сопоставления.</summary>
public class ImportClientMatch
{
    /// <summary>Идентификатор клиента.</summary>
    public Guid Id { get; set; }

    /// <summary>Имя клиента.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Телефон клиента.</summary>
    public string? Phone { get; set; }
}
