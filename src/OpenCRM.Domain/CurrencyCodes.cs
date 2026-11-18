namespace OpenCRM.Domain;

public static class CurrencyCodes
{
    public const string Rub = "RUB";
    public const string Usd = "USD";
    public const string Eur = "EUR";
    public const string Cny = "CNY";

    public static IReadOnlyList<string> Supported { get; } = [Rub, Usd, Eur, Cny];

    public static bool IsSupported(string? currencyCode) =>
        currencyCode is not null && Supported.Contains(currencyCode, StringComparer.Ordinal);
}
