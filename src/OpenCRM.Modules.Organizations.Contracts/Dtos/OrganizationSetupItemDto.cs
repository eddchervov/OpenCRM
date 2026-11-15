using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Элемент настройки: услуга или рабочий пост. </summary>
public sealed class OrganizationSetupItemDto
{
    /// <summary> Идентификатор элемента настройки. </summary>
    public Guid Id { get; set; }

    /// <summary> Название элемента настройки. </summary>
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Цена за единицу, если задана. </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary> Длительность в минутах, если задана. </summary>
    public int? DurationMinutes { get; set; }

    /// <summary> Признак активности элемента. </summary>
    public bool IsActive { get; set; } = true;
}
