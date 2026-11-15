using System.Globalization;
using System.Text.RegularExpressions;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary> Проверяет будущие записи и связи без изменения рабочих данных. </summary>
public static class ImportCandidateValidator
{
    /// <summary> Пересчитывает действия по актуальному состоянию организации. </summary>
    public static void Validate(
        List<ImportCandidate> rows,
        IReadOnlyList<ImportClientMatch> clients,
        IReadOnlyList<(string? Vin, string? Plate)> vehicles)
    {
        foreach (var row in rows)
        {
            row.Status = row.Excluded ? "skip" : "create";
            row.Message = row.Excluded ? "Исключено из импорта." : "Будет создано.";
            row.Matches = [];
            row.LinkedClientName = null;
            row.ResolvedClientId = null;
            row.ResolvedClientRowId = null;
        }

        var clientsById = clients.ToDictionary(client => client.Id);
        var clientsByPhone = clients.Where(client => NormalizePhone(client.Phone).Length > 0)
            .ToLookup(client => NormalizePhone(client.Phone));
        var clientsByName = clients.ToLookup(client => NormalizeName(client.Name));
        var clientRows = rows.Where(row => row.Kind == "client" && !row.Excluded).ToList();

        foreach (var row in clientRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 160 || row.Phone?.Length > 40 ||
                row.Comment?.Length > 600 || row.ExternalId?.Length > 100)
            {
                Mark(row, "error", "Укажите имя до 160 символов, телефон до 40, комментарий до 600 и исходный ID до 100 символов.");
                continue;
            }

            var phone = NormalizePhone(row.Phone);
            row.Matches = (phone.Length > 0 ? clientsByPhone[phone] : clientsByName[NormalizeName(row.Name)]).ToList();
            if (row.ExistingClientId.HasValue)
            {
                if (!clientsById.TryGetValue(row.ExistingClientId.Value, out var selected))
                {
                    Mark(row, "error", "Выбранный клиент недоступен в этой организации.");
                    continue;
                }

                if (row.Matches.All(match => match.Id != selected.Id))
                {
                    row.Matches.Add(selected);
                }

                row.ResolvedClientId = selected.Id;
                row.LinkedClientName = selected.Name;
                Mark(row, "link", "Будет использован выбранный клиент; его поля не изменятся.");
                continue;
            }

            if (row.Matches.Count > 0 && !row.CreateSeparateClient)
            {
                Mark(row, "conflict", "Возможный дубль: выберите существующего клиента, подтвердите отдельную запись или исключите строку.");
            }
            else if (row.CreateSeparateClient)
            {
                row.Message = "Будет создан отдельный клиент по вашему выбору.";
            }
        }

        MarkClientConflicts(clientRows, row => row.ExternalId?.Trim(), "Повторяется исходный ID клиента. Оставьте одну строку или исправьте ID.");
        MarkClientConflicts(clientRows.Where(row => row.Status == "create").ToList(),
            row => NormalizePhone(row.Phone) is { Length: > 0 } phone ? $"phone:{phone}" : $"name:{NormalizeName(row.Name)}",
            "Повтор клиента внутри файла. Оставьте одну строку или явно подтвердите отдельные записи; проверьте связи автомобилей.",
            allowSeparateClients: true);

        var sourceClients = clientRows.Where(row => row.Status is "create" or "link").ToList();
        var sourceById = sourceClients.Where(row => !string.IsNullOrWhiteSpace(row.ExternalId)).ToLookup(row => row.ExternalId!.Trim());
        var sourceByPhone = sourceClients.Where(row => NormalizePhone(row.Phone).Length > 0).ToLookup(row => NormalizePhone(row.Phone));
        var normalizedVehicles = vehicles.Select(vehicle => (Vin: NormalizeKey(vehicle.Vin), Plate: NormalizeKey(vehicle.Plate))).ToList();
        var vehiclesByVin = normalizedVehicles.Where(vehicle => vehicle.Vin.Length > 0).ToLookup(vehicle => vehicle.Vin);
        var vehiclesByPlate = normalizedVehicles.Where(vehicle => vehicle.Plate.Length > 0).ToLookup(vehicle => vehicle.Plate);
        var importedVins = new HashSet<string>();
        var importedPlates = new HashSet<string>();
        var vehicleRows = rows.Where(row => row.Kind == "vehicle" && !row.Excluded).ToList();

        foreach (var row in vehicleRows)
        {
            if (!IsValidVehicle(row))
            {
                Mark(row, "error", $"Проверьте название (до 160), марку и модель (до 100), VIN (5–32 латинских букв/цифр), номер (2–16) и год (1900–{DateTime.UtcNow.Year}). HTML не допускается.");
                continue;
            }

            var vin = NormalizeKey(row.Vin);
            var plate = NormalizeKey(row.Plate);
            if (vin.Length == 0 && plate.Length == 0)
            {
                Mark(row, "error", "Для автомобиля требуется VIN или госномер.");
                continue;
            }

            var matches = vehiclesByVin[vin].Concat(vehiclesByPlate[plate]).Distinct().ToList();
            if (matches.Count > 1 || matches.Any(vehicle =>
                vin.Length > 0 && vehicle.Vin.Length > 0 && vin != vehicle.Vin ||
                plate.Length > 0 && vehicle.Plate.Length > 0 && plate != vehicle.Plate))
            {
                Mark(row, "conflict", "VIN и госномер противоречат существующим автомобилям. Проверьте идентификаторы или исключите строку.");
                continue;
            }

            if (matches.Count == 1)
            {
                Mark(row, "duplicate", "Автомобиль уже существует. Строка будет пропущена без изменения его данных и владельца.");
                continue;
            }

            if (vin.Length > 0 && importedVins.Contains(vin) || plate.Length > 0 && importedPlates.Contains(plate))
            {
                Mark(row, "error", "Повтор автомобиля внутри файла. Исключите лишнюю строку или исправьте идентификаторы.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.ClientExternalId))
            {
                var matchingRows = sourceById[row.ClientExternalId.Trim()].ToList();
                if (matchingRows.Count != 1)
                {
                    Mark(row, "error", "Исходный ID должен соответствовать одному включённому и проверенному клиенту из файла.");
                    continue;
                }

                Link(row, matchingRows[0]);
            }
            else if (NormalizePhone(row.ClientPhone) is { Length: > 0 } clientPhone)
            {
                var matchingRows = sourceByPhone[clientPhone].ToList();
                var existingMatches = clientsByPhone[clientPhone].ToList();
                if (matchingRows.Count == 1 && (existingMatches.Count == 0 || matchingRows[0].ExistingClientId.HasValue &&
                    existingMatches.All(client => client.Id == matchingRows[0].ExistingClientId)))
                {
                    Link(row, matchingRows[0]);
                }
                else if (matchingRows.Count == 0 && existingMatches.Count == 1)
                {
                    row.ResolvedClientId = existingMatches[0].Id;
                    row.LinkedClientName = existingMatches[0].Name;
                }
                else
                {
                    Mark(row, "error", "Телефон владельца неоднозначен или клиент не найден. Укажите исходный ID проверенного клиента.");
                    continue;
                }
            }

            if (vin.Length > 0)
            {
                importedVins.Add(vin);
            }

            if (plate.Length > 0)
            {
                importedPlates.Add(plate);
            }
        }
    }

    private static bool IsValidVehicle(ImportCandidate row) =>
        row.Name.Length <= 160 && row.Brand?.Length is not > 100 && row.Model?.Length is not > 100 &&
        IsSafeText(row.Name) && IsSafeText(row.Brand) && IsSafeText(row.Model) &&
        (string.IsNullOrWhiteSpace(row.Vin) || Regex.IsMatch(row.Vin, "^[A-Za-z0-9]{5,32}$")) &&
        (string.IsNullOrWhiteSpace(row.Plate) || Regex.IsMatch(row.Plate, "^[A-Za-zА-Яа-яЁё0-9\\-\\s]{2,16}$")) &&
        row.ClientExternalId?.Length is not > 100 &&
        row.ClientPhone?.Length is not > 40 && (string.IsNullOrWhiteSpace(row.Year) ||
            int.TryParse(row.Year, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1900 && year <= DateTime.UtcNow.Year);

    private static bool IsSafeText(string? value) => value == null ||
        !value.Contains('<') && !value.Contains('>') && !value.Contains("javascript:", StringComparison.OrdinalIgnoreCase);

    private static void MarkClientConflicts(
        List<ImportCandidate> rows,
        Func<ImportCandidate, string?> key,
        string message,
        bool allowSeparateClients = false)
    {
        var groups = rows.Where(row => row.Status != "error" && !string.IsNullOrWhiteSpace(key(row)))
            .GroupBy(key).Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            if (allowSeparateClients && group.All(row => row.CreateSeparateClient))
            {
                continue;
            }

            foreach (var row in group)
            {
                Mark(row, "conflict", message);
            }
        }
    }

    private static void Link(ImportCandidate vehicle, ImportCandidate client)
    {
        vehicle.ResolvedClientId = client.ExistingClientId;
        vehicle.ResolvedClientRowId = client.ExistingClientId.HasValue ? null : client.Id;
        vehicle.LinkedClientName = client.LinkedClientName ?? client.Name;
    }

    private static void Mark(ImportCandidate row, string status, string message)
    {
        row.Status = status;
        row.Message = message;
    }

    private static string NormalizePhone(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizeKey(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
