namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Профиль формы создания обслуживания для сценария рабочего пространства. </summary>
public sealed record OrganizationWorkspaceMaintenanceFormProfileDto
{
    /// <summary> Режим формы. </summary>
    public string Mode { get; init; } = "full-service";

    /// <summary> Признак предпочтения быстрой формы визита. </summary>
    public bool PreferQuickVisitForm { get; init; }

    /// <summary> Признак отображения пробега. </summary>
    public bool ShowMileage { get; init; } = true;

    /// <summary> Признак отображения материалов. </summary>
    public bool ShowMaterials { get; init; } = true;

    /// <summary> Признак отображения связанных дефектов. </summary>
    public bool ShowLinkedDefects { get; init; } = true;

    /// <summary> Признак отображения фотографий. </summary>
    public bool ShowPhotos { get; init; } = true;

    /// <summary> Признак отображения документов. </summary>
    public bool ShowDocuments { get; init; } = true;

    /// <summary> Признак отображения исполнителя. </summary>
    public bool ShowPerformer { get; init; } = true;

    /// <summary> Признак отображения места проведения. </summary>
    public bool ShowLocation { get; init; } = true;
}
