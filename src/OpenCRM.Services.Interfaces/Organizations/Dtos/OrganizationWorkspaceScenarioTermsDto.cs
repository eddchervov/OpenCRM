namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Терминология сценария рабочего пространства. </summary>
public sealed record OrganizationWorkspaceScenarioTermsDto
{
    /// <summary> Термин для одного транспортного средства. </summary>
    public string Vehicle { get; init; } = "Техника";

    /// <summary> Термин для нескольких транспортных средств. </summary>
    public string Vehicles { get; init; } = "Техника";

    /// <summary> Термин для одного клиента. </summary>
    public string Client { get; init; } = "Клиент";

    /// <summary> Термин для нескольких клиентов. </summary>
    public string Clients { get; init; } = "Клиенты";

    /// <summary> Термин для обслуживания. </summary>
    public string Maintenance { get; init; } = "Обслуживание";

    /// <summary> Термин для нескольких обслуживаний. </summary>
    public string Maintenances { get; init; } = "Обслуживания";

    /// <summary> Термин для дефекта. </summary>
    public string Defect { get; init; } = "Неисправность";

    /// <summary> Термин для дефектов. </summary>
    public string Defects { get; init; } = "Неисправности";

    /// <summary> Термин для счётчиков. </summary>
    public string Counters { get; init; } = "Пробег и наработка";

    /// <summary> Термин для планировщика. </summary>
    public string Scheduler { get; init; } = "Планирование";

    /// <summary> Термин для материалов. </summary>
    public string Materials { get; init; } = "Склад";

    /// <summary> Термин для документов. </summary>
    public string Documents { get; init; } = "Документы";

    /// <summary> Термин для журнала работ. </summary>
    public string WorkJournal { get; init; } = "Журнал работ";

    /// <summary> Термин для участников. </summary>
    public string Members { get; init; } = "Участники";

    /// <summary> Термин для биллинга. </summary>
    public string Billing { get; init; } = "Тарифы";

    /// <summary> Термин для вики. </summary>
    public string Wiki { get; init; } = "Вики";

    /// <summary> Термин для настроек. </summary>
    public string Settings { get; init; } = "Настройки";
}
