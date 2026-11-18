namespace OpenCRM.Admin.Api.Models;

public sealed record AdminCrmImportRow(int Number, string Type, string Name, string? Phone, string? Brand,
    string? Model, string? Vin, string? Plate, string Status, string? Message, string? RegistrationCountryCode = null,
    string? MessageCode = null, IReadOnlyDictionary<string, string>? MessageParameters = null)
{
    public string Label => string.Join(" · ", new[] { this.Name, this.Phone, this.Brand, this.Model, this.Vin, this.RegistrationCountryCode, this.Plate }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string StatusLabel => this.Status switch { "create" => "Будет добавлено", "duplicate" => "Дубль", _ => "Ошибка" };
}
