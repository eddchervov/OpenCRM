
namespace OpenCRM.Modules.Maintenance;

/// <summary> Коды событий истории обслуживания. </summary>
internal static class MaintenanceHistoryEventCodes
{
    /// <summary> Код события создания обслуживания. </summary>
    internal const string EventCreated = "created";

    /// <summary> Код события изменения обслуживания. </summary>
    internal const string EventUpdated = "updated";

    /// <summary> Код события смены статуса. </summary>
    internal const string EventStatusChanged = "status_changed";

    /// <summary> Код события удаления обслуживания. </summary>
    internal const string EventDeleted = "deleted";

    /// <summary> Код события привязки дефекта. </summary>
    internal const string EventDefectLinked = "defect_linked";

    /// <summary> Код события отвязки дефекта. </summary>
    internal const string EventDefectUnlinked = "defect_unlinked";

    /// <summary> Код события изменения полей. </summary>
    internal const string EventFieldsChanged = "fields_changed";

    /// <summary> Код события изменения состава работ. </summary>
    internal const string EventDetailsChanged = "details_changed";

    /// <summary> Код события формирования документа. </summary>
    internal const string EventDocumentGenerated = "document_generated";
}
