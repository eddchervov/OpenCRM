namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Р‘С‹СЃС‚СЂРѕРµ РґРµР№СЃС‚РІРёРµ СЂР°Р±РѕС‡РµРіРѕ РїСЂРѕС†РµСЃСЃР° РѕР±СЃР»СѓР¶РёРІР°РЅРёСЏ. </summary>
public class MaintenanceWorkflowActionDto
{
    /// <summary> Действие рабочего процесса обслуживания. </summary>
    public required string Action { get; set; }

    /// <summary>Версия записи, показанная пользователю перед действием.</summary>
    public uint? Version { get; set; }

    /// <summary>Новый пост при переносе: null сохраняет текущий, пустая строка снимает назначение.</summary>
    public string? Location { get; set; }

    /// <summary> РќРѕРІР°СЏ РїР»Р°РЅРѕРІР°СЏ РґР°С‚Р° РґР»СЏ РїРµСЂРµРЅРѕСЃР° РёР»Рё РѕС‚РјРµРЅС‹. </summary>
    public DateTime? PlannedDate { get; set; }

    /// <summary> Плановая дата в текстовом виде. </summary>
    public string? PlannedDateText { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Плановое время начала в текстовом виде. </summary>
    public string? PlannedStartTimeText { get; set; }

    /// <summary> Плановое время окончания в текстовом виде. </summary>
    public string? PlannedEndTimeText { get; set; }

    /// <summary> Р¤Р°РєС‚РёС‡РµСЃРєР°СЏ РґР°С‚Р° РІС‹РїРѕР»РЅРµРЅРёСЏ. </summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary> Дата завершения в текстовом виде. </summary>
    public string? CompletedDateText { get; set; }

    /// <summary> РџСЂРѕР±РµРі/РЅР°СЂР°Р±РѕС‚РєР° РЅР° РјРѕРјРµРЅС‚ РІС‹РїРѕР»РЅРµРЅРёСЏ. </summary>
    public int? Mileage { get; set; }

    /// <summary> РџСЂРёС‡РёРЅР° РґРµР№СЃС‚РІРёСЏ РёР»Рё РєРѕСЂРѕС‚РєРёР№ РєРѕРјРјРµРЅС‚Р°СЂРёР№. </summary>
    public string? Reason { get; set; }
}

