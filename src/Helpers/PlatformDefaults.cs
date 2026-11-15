namespace Helpers;

/// <summary>
/// Нейтральные значения базовой поставки платформы.
/// Конкретные квоты, тарифы и коммерческие правила задаёт владелец установки:
/// поставка их не навязывает. Значение <c>0</c> означает «без ограничения».
/// </summary>
public static class PlatformDefaults
{
    /// <summary> Лимит техники без оформленного тарифа. 0 — без ограничения. </summary>
    public const int VehicleLimit = 0;

    /// <summary> Лимит клиентов рабочего пространства. 0 — без ограничения. </summary>
    public const int ClientLimit = 0;

    /// <summary> Лимит участников рабочего пространства. 0 — без ограничения. </summary>
    public const int WorkspaceMembersLimit = 0;

    /// <summary>
    /// Приводит лимит к значению «без ограничения», если ограничение не задано.
    /// Нулевое и отрицательное значение означают отсутствие квоты, а не запрет на записи.
    /// </summary>
    public static int NormalizeLimit(int limit) => limit > 0 ? limit : int.MaxValue;
}
