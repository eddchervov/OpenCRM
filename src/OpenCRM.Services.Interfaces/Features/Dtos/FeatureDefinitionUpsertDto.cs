using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Данные для создания или обновления описания функциональной возможности. </summary>
public class FeatureDefinitionUpsertDto
{
    /// <summary> Код функциональной возможности. </summary>
    [Required]
    [StringLength(100)]
    public string Code { get; set; } = string.Empty;

    /// <summary> Наименование функциональной возможности. </summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание функциональной возможности. </summary>
    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary> Категория функциональной возможности. </summary>
    [StringLength(100)]
    public string? Category { get; set; }

    /// <summary> Код области действия возможности. </summary>
    [Required]
    public string ScopeCode { get; set; } = OpenCRM.Domain.FeatureCatalogCodes.Scopes.Both;

    /// <summary> Код аудитории возможности. </summary>
    [Required]
    public string AudienceCode { get; set; } = OpenCRM.Domain.FeatureCatalogCodes.Audiences.User;

    /// <summary> Тип значения возможности. </summary>
    [Required]
    public string ValueType { get; set; } = "Boolean";

    /// <summary> Признак активности функциональной возможности. </summary>
    public bool IsActive { get; set; } = true;
}

