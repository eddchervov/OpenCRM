namespace OpenCRM.Domain;

public static class MaintenanceVatRates
{
    public const string WithoutVat = "without_vat";

    public static bool IsValid(string? code) => code is WithoutVat or "0" or "5" or "7" or "10" or "20" or "22";

    public static decimal? GetPercent(string? code) => code switch
    {
        "0" => 0m,
        "5" => 5m,
        "7" => 7m,
        "10" => 10m,
        "20" => 20m,
        "22" => 22m,
        _ => null
    };
}
