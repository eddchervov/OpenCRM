namespace OpenCRM.Domain;

/// <summary>
/// Элемент сценарного справочника организации: услуга, документ, правило планирования или материал.
/// </summary>
public class OrganizationScenarioCatalogItem
{
    /// <summary>Идентификатор элемента справочника.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Идентификатор организации, которой принадлежит элемент.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Организация-владелец справочника.</summary>
    public Organization Organization { get; set; } = null!;

    /// <summary>Код сценария организации, из которого создан элемент.</summary>
    public string ScenarioCode { get; set; } = string.Empty;

    /// <summary>Тип справочника: работа, документ, правило планирования или материал.</summary>
    public string KindCode { get; set; } = OrganizationScenarioCatalogKindCodes.WorkType;

    /// <summary>Стабильный код элемента внутри организации и типа справочника.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Название, которое показывается пользователю в формах и настройках.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Дополнительное описание элемента для будущих UI и API-клиентов.</summary>
    public string? Description { get; set; }

    /// <summary> Цена единицы элемента сценарного справочника. </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary> Длительность выполнения элемента справочника в минутах. </summary>
    public int? DurationMinutes { get; set; }

    /// <summary> Список позиций, входящих в пакет услуг элемента справочника. </summary>
    public List<OrganizationServiceBundleItem> BundleItems { get; set; } = [];

    /// <summary>Стабильный тип генератора для документа scenario-catalog.</summary>
    public string? DocumentKindCode { get; set; }

    /// <summary>Порядок отображения внутри группы справочника.</summary>
    public int SortOrder { get; set; }

    /// <summary>Признак системного элемента, созданного из дефолтного сценария.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Признак активности элемента в формах и списках выбора.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Признак хотя бы одного успешного использования; после установки не сбрасывается.</summary>
    public bool HasBeenUsed { get; set; }

    /// <summary>Дата создания элемента справочника.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата последнего изменения элемента справочника.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
