namespace OpenCRM.Domain;

/// <summary> Справочник кодов типов тарифных планов подписки. </summary>
public static class SubscriptionPlanCatalogCodes
{
    /// <summary> Постоянная группа бесплатного тарифа организаций детейлинга и СТО. </summary>
    public const string OrganizationFreeGroup = "workspace.free.v1";

    /// <summary> Коды типов тарифных планов. </summary>
    public static class PlanTypes
    {
        /// <summary> Код личного тарифного плана. </summary>
        public const string Personal = "personal";

        /// <summary> Код бизнес-тарифного плана. </summary>
        public const string Business = "business";

        /// <summary> Список всех кодов типов тарифных планов. </summary>
        public static readonly IReadOnlyCollection<string> All =
        [
            Personal,
            Business
        ];
    }

    /// <summary> Возвращает название типа тарифного плана по его коду. </summary>
    public static string GetPlanTypeName(string? code) => NormalizePlanType(code) switch
    {
        PlanTypes.Business => "Бизнес",
        _ => "Личный"
    };

    /// <summary> Приводит код типа тарифного плана к нормализованному виду. </summary>
    public static string NormalizePlanType(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return PlanTypes.All.Contains(normalized ?? string.Empty)
            ? normalized!
            : PlanTypes.Personal;
    }
}
