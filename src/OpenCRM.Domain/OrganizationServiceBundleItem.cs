namespace OpenCRM.Domain;

/// <summary> Позиция пакета услуг в сценарном справочнике организации. </summary>
public sealed class OrganizationServiceBundleItem
{
    /// <summary> Идентификатор пакета услуг. </summary>
    public Guid BundleId { get; set; }

    /// <summary> Пакет услуг, в который входит позиция. </summary>
    public OrganizationScenarioCatalogItem Bundle { get; set; } = default!;

    /// <summary> Идентификатор услуги. </summary>
    public Guid ServiceId { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Код вида пакета услуг. </summary>
    public string BundleKindCode { get; set; } = OrganizationScenarioCatalogKindCodes.ServiceBundle;

    /// <summary> Код вида услуги в позиции пакета. </summary>
    public string ServiceKindCode { get; set; } = OrganizationScenarioCatalogKindCodes.Service;

    /// <summary> Порядковый номер позиции в пакете. </summary>
    public int Position { get; set; }
}
