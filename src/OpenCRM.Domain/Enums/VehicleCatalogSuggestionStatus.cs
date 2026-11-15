namespace OpenCRM.Domain.Enums;

/// <summary> Статус предложения в каталог техники. </summary>
public enum VehicleCatalogSuggestionStatus
{
    /// <summary> Предложение ожидает модерации. </summary>
    Pending = 1,

    /// <summary> Предложение одобрено и добавлено в каталог. </summary>
    Approved = 2,

    /// <summary> Предложение отклонено модератором. </summary>
    Rejected = 3,
}
