namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Доступные модули сценария рабочего пространства. </summary>
public sealed record OrganizationWorkspaceScenarioModulesDto
{
    /// <summary> Признак доступности расширенной приёмки. </summary>
    public bool Intake { get; init; }

    /// <summary> Признак доступности новых запросов ИИ-помощнику. </summary>
    public bool Assistant { get; init; }

    /// <summary> Признак показа помощника на сайте с учётом общего выключателя администратора. </summary>
    public bool AssistantVisible { get; init; } = true;

    /// <summary> Признак доступности подключения ботов и отправки клиентских сообщений. </summary>
    public bool Messaging { get; init; }

    /// <summary> Признак доступности новых фотографий. </summary>
    public bool Photos { get; init; }

    /// <summary> Признак доступности модуля транспортных средств. </summary>
    public bool Vehicles { get; init; }

    /// <summary> Признак доступности модуля клиентов. </summary>
    public bool Clients { get; init; }

    /// <summary> Признак доступности модуля обслуживаний. </summary>
    public bool Maintenance { get; init; }

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool Defects { get; init; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool Counters { get; init; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool Scheduler { get; init; }

    /// <summary> Признак доступности модуля материалов. </summary>
    public bool Materials { get; init; }

    /// <summary> Признак доступности модуля документов. </summary>
    public bool Documents { get; init; }

    /// <summary> Признак доступности модуля журнала работ. </summary>
    public bool WorkJournal { get; init; }

    /// <summary> Признак доступности модуля финансов. </summary>
    public bool Finance { get; init; }

    /// <summary> Признак доступности подготовки выгрузки для 1С:Бухгалтерии 3.0. </summary>
    public bool AccountingExport { get; init; }

    /// <summary> Признак доступности модуля аналитики. </summary>
    public bool Analytics { get; init; }

    /// <summary> Признак доступности модуля вики. </summary>
    public bool Wiki { get; init; }

    /// <summary> Признак доступности модуля участников. </summary>
    public bool Members { get; init; }

    /// <summary> Признак доступности модуля биллинга. </summary>
    public bool Billing { get; init; }

    /// <summary> Признак доступности модуля настроек. </summary>
    public bool Settings { get; init; }
}
