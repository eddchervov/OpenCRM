namespace OpenCRM.Domain;

/// <summary> Связь промокода со сценарием. </summary>
public class PromoCodeScenario
{
    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Промокод, связанный со сценарием. </summary>
    public PromoCode PromoCode { get; set; } = null!;

    /// <summary> Код сценария, к которому применим промокод. </summary>
    public string ScenarioCode { get; set; } = string.Empty;
}
