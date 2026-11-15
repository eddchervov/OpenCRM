namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> РЎРѕР±С‹С‚РёРµ РёСЃС‚РѕСЂРёРё С‚РµС…РЅРёРєРё. </summary>
public class VehicleHistoryEventDto
{
    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ СЃРѕР±С‹С‚РёСЏ. </summary>
    public Guid Id { get; set; }

    /// <summary> РўРёРї СЃРѕР±С‹С‚РёСЏ. </summary>
    public string EventType { get; set; } = default!;

    /// <summary> Р—Р°РіРѕР»РѕРІРѕРє СЃРѕР±С‹С‚РёСЏ. </summary>
    public string Title { get; set; } = default!;

    /// <summary> РџРѕРґСЂРѕР±РЅРѕСЃС‚Рё СЃРѕР±С‹С‚РёСЏ. </summary>
    public string? Description { get; set; }

    /// <summary> РћС‚РѕР±СЂР°Р¶Р°РµРјРѕРµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ РЅР° РјРѕРјРµРЅС‚ СЃРѕР±С‹С‚РёСЏ. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> РўРёРї СЃРІСЏР·Р°РЅРЅРѕР№ СЃСѓС‰РЅРѕСЃС‚Рё. </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ СЃРІСЏР·Р°РЅРЅРѕР№ СЃСѓС‰РЅРѕСЃС‚Рё. </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary> РЎСЃС‹Р»РєР° РЅР° СЃРІСЏР·Р°РЅРЅСѓСЋ СЃСѓС‰РЅРѕСЃС‚СЊ. </summary>
    public string? RelatedUrl { get; set; }

    /// <summary> Р”Р°С‚Р° СЃРѕР·РґР°РЅРёСЏ СЃРѕР±С‹С‚РёСЏ. </summary>
    public DateTime CreatedAt { get; set; }
}
