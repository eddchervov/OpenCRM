using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> РЎРѕР±С‹С‚РёРµ РёСЃС‚РѕСЂРёРё С‚РµС…РЅРёРєРё. </summary>
public class VehicleHistoryEvent
{
    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ СЃРѕР±С‹С‚РёСЏ. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ С‚РµС…РЅРёРєРё. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> РўРµС…РЅРёРєР°. </summary>
    public Vehicle Vehicle { get; set; } = default!;

    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ РѕСЂРіР°РЅРёР·Р°С†РёРё. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ, РІС‹РїРѕР»РЅРёРІС€РµРіРѕ РґРµР№СЃС‚РІРёРµ. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> РџРѕР»СЊР·РѕРІР°С‚РµР»СЊ, РІС‹РїРѕР»РЅРёРІС€РёР№ РґРµР№СЃС‚РІРёРµ. </summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary> РћС‚РѕР±СЂР°Р¶Р°РµРјРѕРµ РёРјСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ РЅР° РјРѕРјРµРЅС‚ СЃРѕР±С‹С‚РёСЏ. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> РўРёРї СЃРѕР±С‹С‚РёСЏ. </summary>
    public string EventType { get; set; } = default!;

    /// <summary> Р—Р°РіРѕР»РѕРІРѕРє СЃРѕР±С‹С‚РёСЏ. </summary>
    public string Title { get; set; } = default!;

    /// <summary> РџРѕРґСЂРѕР±РЅРѕСЃС‚Рё СЃРѕР±С‹С‚РёСЏ. </summary>
    public string? Description { get; set; }

    /// <summary> РўРёРї СЃРІСЏР·Р°РЅРЅРѕР№ СЃСѓС‰РЅРѕСЃС‚Рё. </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary> РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ СЃРІСЏР·Р°РЅРЅРѕР№ СЃСѓС‰РЅРѕСЃС‚Рё. </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary> Р”Р°С‚Р° СЃРѕР·РґР°РЅРёСЏ СЃРѕР±С‹С‚РёСЏ. </summary>
    public DateTime CreatedAt { get; set; }
}

