namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные условного изменения валюты. </summary>
public sealed class UpdateCurrencySettingsDto
{
    public string CurrencyCode { get; init; } = string.Empty;

    public string ExpectedCurrencyCode { get; init; } = string.Empty;
}
