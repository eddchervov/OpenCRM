namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Валюта и доступность её изменения. </summary>
public sealed class CurrencySettingsDto
{
    public string CurrencyCode { get; init; } = "RUB";

    public bool CanChange { get; init; }

    public bool CanManage { get; init; }

    public IReadOnlyList<string> SupportedCurrencies { get; init; } = OpenCRM.Domain.CurrencyCodes.Supported;
}
