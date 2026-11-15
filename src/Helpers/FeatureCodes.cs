namespace Helpers;

/// <summary> Справочник кодов функций тарифных возможностей. </summary>
public static class FeatureCodes
{
    /// <summary> Код лимита клиентов организации. </summary>
    public const string WorkspaceClientsLimit = "workspace.clients.limit";

    /// <summary> Код расширенной приёмки автомобиля. </summary>
    public const string MaintenanceIntake = "workspace.maintenance.intake";

    /// <summary> Код серверного ИИ-помощника. </summary>
    public const string Assistant = "workspace.assistant";

    /// <summary> Код клиентских ботов и автоматических сообщений. </summary>
    public const string ClientMessaging = "workspace.client_messaging";

    /// <summary> Код настройки оформления документов организации. </summary>
    public const string DocumentStyling = "workspace.documents.styling";

    /// <summary> Код импорта данных организации. </summary>
    public const string DataImport = "workspace.data.import";

    /// <summary> Код подготовки выгрузки для 1С:Бухгалтерии 3.0. </summary>
    public const string FinanceExport1C = "workspace.finance.export_1c";

    /// <summary> Код ограничения числа единиц техники. </summary>
    public const string VehiclesLimit = "vehicles.limit";

    /// <summary> Код лимита техники рабочего пространства. </summary>
    public const string WorkspaceVehiclesLimit = "workspace.vehicles.limit";

    /// <summary> Код лимита участников рабочего пространства. </summary>
    public const string WorkspaceMembersLimit = "workspace.members.limit";

    /// <summary> Код модуля техники. </summary>
    public const string VehiclesModule = "workspace.module.vehicles";

    /// <summary> Код экспорта в Word. </summary>
    public const string ExportWord = "export.word";

    /// <summary> Код экспорта в Word с фотографиями. </summary>
    public const string ExportWordWithPhotos = "export.word.with_photos";

    /// <summary> Код экспорта в PDF. </summary>
    public const string ExportPdf = "export.pdf";

    /// <summary> Код экспорта в PDF с фотографиями. </summary>
    public const string ExportPdfWithPhotos = "export.pdf.with_photos";

    /// <summary> Код фотоальбомов техники. </summary>
    public const string VehiclePhotoAlbums = "vehicle.photo_albums";

    /// <summary> Код модуля клиентов. </summary>
    public const string ClientsModule = "workspace.module.clients";

    /// <summary> Код модуля дефектов. </summary>
    public const string DefectsModule = "workspace.module.defects";

    /// <summary> Код модуля счётчиков. </summary>
    public const string CountersModule = "workspace.module.counters";

    /// <summary> Код модуля обслуживания. </summary>
    public const string MaintenanceModule = "workspace.module.maintenance";

    /// <summary> Код модуля планировщика. </summary>
    public const string SchedulerModule = "workspace.module.scheduler";

    /// <summary> Код модуля напоминаний. </summary>
    public const string RemindersModule = "workspace.module.reminders";

    /// <summary> Код модуля документов. </summary>
    public const string DocumentsModule = "workspace.module.documents";

    /// <summary> Код модуля материалов. </summary>
    public const string MaterialsModule = "workspace.module.materials";

    /// <summary> Код модуля журнала работ. </summary>
    public const string WorkJournalModule = "workspace.module.work_journal";

    /// <summary> Код модуля финансов. </summary>
    public const string FinanceModule = "workspace.module.finance";

    /// <summary> Код модуля аналитики. </summary>
    public const string AnalyticsModule = "workspace.module.analytics";

    /// <summary> Код модуля базы знаний. </summary>
    public const string WikiModule = "workspace.module.wiki";

    /// <summary> Код модуля участников. </summary>
    public const string MembersModule = "workspace.module.members";

    /// <summary> Код модуля биллинга. </summary>
    public const string BillingModule = "workspace.module.billing";

    /// <summary> Код модуля настроек. </summary>
    public const string SettingsModule = "workspace.module.settings";

    /// <summary> Массив всех кодов функций. </summary>
    public static readonly string[] All =
    [
        WorkspaceClientsLimit,
        MaintenanceIntake,
        Assistant,
        ClientMessaging,
        DocumentStyling,
        DataImport,
        FinanceExport1C,
        VehiclesLimit,
        WorkspaceVehiclesLimit,
        WorkspaceMembersLimit,
        VehiclesModule,
        ExportWord,
        ExportWordWithPhotos,
        ExportPdf,
        ExportPdfWithPhotos,
        VehiclePhotoAlbums,
        ClientsModule,
        DefectsModule,
        CountersModule,
        MaintenanceModule,
        SchedulerModule,
        RemindersModule,
        DocumentsModule,
        MaterialsModule,
        WorkJournalModule,
        FinanceModule,
        AnalyticsModule,
        WikiModule,
        MembersModule,
        BillingModule,
        SettingsModule
    ];
}
