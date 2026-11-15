using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Crm;

/// <summary> Сервис импорта клиентов и техники из CRM-файлов. </summary>
public sealed class CrmImportService(
    OpenCRMContext opencrmContext,
    IVehicleLimitQueryService vehicleLimits,
    IOrganizationSubscriptionService subscriptions) : ICrmImportService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;
    private readonly IVehicleLimitQueryService _vehicleLimits = vehicleLimits;
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;

    /// <summary> Ищет пользователей по строке поиска. </summary>
    public async Task<object> SearchUsersAsync(string? search, CancellationToken cancellationToken)
    {
        var query = this._opencrmContext.ApplicationUsers.AsNoTracking().Where(user => !user.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user => user.Email != null && EF.Functions.ILike(user.Email, $"%{term}%") ||
                user.PhoneNumber != null && EF.Functions.ILike(user.PhoneNumber, $"%{term}%"));
        }

        return await query.OrderBy(user => user.Email).Take(30)
            .Select(user => new { user.Id, user.Email, Phone = user.PhoneNumber })
            .ToListAsync(cancellationToken);
    }

    /// <summary> Возвращает организации пользователя. </summary>
    public async Task<object> GetOrganizationsAsync(string userId, CancellationToken cancellationToken)
    {
        return await this._opencrmContext.Organizations.AsNoTracking()
            .Where(organization => !organization.IsDeleted && !organization.IsArchived &&
                (organization.OwnerUserId == userId || organization.Members.Any(member => member.UserId == userId)))
            .OrderBy(organization => organization.Name)
            .Select(organization => new
            {
                organization.Id,
                organization.Name,
                OwnerEmail = organization.OwnerUser.Email
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary> Создаёт предпросмотр импорта из загруженного файла. </summary>
    public async Task<CrmImportPreview> CreatePreviewAsync(
        string userId,
        Guid organizationId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (content.Length == 0 || content.Length > 10 * 1024 * 1024)
        {
            throw new InvalidDataException("Размер файла должен быть от 1 байта до 10 МБ.");
        }

        var organization = await this._opencrmContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive &&
                (item.OwnerUserId == userId || item.Members.Any(member => member.UserId == userId)), cancellationToken);
        if (organization == null)
        {
            throw new InvalidDataException("Пользователь не состоит в выбранной активной организации.");
        }

        var rows = CrmImportParser.Parse(content, fileName).ToList();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(rows.Select(row => new
            {
                row.Type, row.Name, row.Phone, row.ClientPhone, row.Brand, row.Model, row.Vin, row.Plate, row.Year, row.YearText
            }).OrderBy(row => JsonSerializer.Serialize(row))))));
        var alreadyImported = await this._opencrmContext.CrmImportBatches.AsNoTracking()
            .AnyAsync(batch => batch.OrganizationId == organizationId && batch.Fingerprint == fingerprint &&
                batch.ImportedAt != null, cancellationToken);

        await this.ValidateRowsAsync(organizationId, rows, cancellationToken);

        var batch = new CrmImportBatch
        {
            OrganizationId = organizationId,
            UserId = userId,
            FileName = Path.GetFileName(fileName),
            Fingerprint = fingerprint,
            RowsJson = JsonSerializer.Serialize(rows)
        };
        this._opencrmContext.CrmImportBatches.Add(batch);
        await this._opencrmContext.SaveChangesAsync(cancellationToken);

        return CreatePreview(batch, rows, alreadyImported);
    }

    /// <summary> Возвращает сохранённый предпросмотр импорта по идентификатору. </summary>
    public async Task<CrmImportPreview?> GetPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await this._opencrmContext.CrmImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (batch == null)
        {
            return null;
        }

        var rows = JsonSerializer.Deserialize<List<CrmImportRow>>(batch.RowsJson) ?? [];
        var alreadyImported = await this._opencrmContext.CrmImportBatches.AsNoTracking()
            .AnyAsync(item => item.OrganizationId == batch.OrganizationId && item.Fingerprint == batch.Fingerprint &&
                item.ImportedAt != null, cancellationToken);

        return CreatePreview(batch, rows, alreadyImported);
    }

    /// <summary> Подтверждает импорт и создаёт клиентов и технику. </summary>
    public async Task<CrmImportResult> ConfirmAsync(Guid id, string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new UnauthorizedAccessException("Не удалось определить администратора, подтвердившего импорт.");
        }

        CrmImportResult? result = null;
        var isRetry = false;
        await this._opencrmContext.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                this._opencrmContext.ChangeTracker.Clear();
            }

            isRetry = true;
            result = await this.ConfirmCoreAsync(id, actorUserId, cancellationToken);
        }, () => this._opencrmContext.CrmImportBatches.AsNoTracking()
            .AnyAsync(batch => batch.Id == id && batch.ImportedAt != null, cancellationToken));

        return result ?? throw new InvalidOperationException("Не удалось получить результат импорта.");
    }

    /// <summary> Выполняет основную логику подтверждения импорта. </summary>
    private async Task<CrmImportResult> ConfirmCoreAsync(Guid id, string actorUserId, CancellationToken cancellationToken)
    {
        var batchHeader = await this._opencrmContext.CrmImportBatches.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.Id, item.OrganizationId })
            .FirstOrDefaultAsync(cancellationToken);
        if (batchHeader == null)
        {
            throw new KeyNotFoundException("Предпросмотр не найден.");
        }

        await this._opencrmContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {batchHeader.OrganizationId} FOR UPDATE", cancellationToken);
        await this._opencrmContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"CrmImportBatches\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
        var batch = await this._opencrmContext.CrmImportBatches.FirstOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == batchHeader.OrganizationId, cancellationToken);
        if (batch == null)
        {
            throw new KeyNotFoundException("Предпросмотр не найден.");
        }

        if (batch.ImportedAt != null || await this._opencrmContext.CrmImportBatches.AsNoTracking()
                .AnyAsync(item => item.Id != id && item.OrganizationId == batch.OrganizationId &&
                    item.Fingerprint == batch.Fingerprint && item.ImportedAt != null, cancellationToken))
        {
            throw new InvalidDataException("Эти данные уже импортированы в организацию.");
        }

        var organization = await this._opencrmContext.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == batch.OrganizationId && !item.IsDeleted && !item.IsArchived && item.IsActive &&
                (item.OwnerUserId == batch.UserId || item.Members.Any(member => member.UserId == batch.UserId)), cancellationToken);
        if (organization == null)
        {
            throw new InvalidDataException("Организация или связь с пользователем изменилась. Создайте новый предпросмотр.");
        }

        var rows = JsonSerializer.Deserialize<List<CrmImportRow>>(batch.RowsJson) ?? [];
        if (rows.Any(row => row.Status == "error"))
        {
            throw new InvalidDataException("В предпросмотре есть ошибки. Исправьте файл и проверьте его заново.");
        }

        var currentRows = rows.Select(row => new CrmImportRow
        {
            Number = row.Number, Type = row.Type, Name = row.Name, Phone = row.Phone,
            ClientPhone = row.ClientPhone, Brand = row.Brand, Model = row.Model,
            Vin = row.Vin, Plate = row.Plate, Year = row.Year, YearText = row.YearText
        }).ToList();
        await this.ValidateRowsAsync(batch.OrganizationId, currentRows, cancellationToken);
        if (rows.Where(row => row.Status == "create").Select(row => row.Number)
            .Except(currentRows.Where(row => row.Status == "create").Select(row => row.Number)).Any())
        {
            throw new InvalidDataException("Данные организации изменились. Создайте новый предпросмотр.");
        }

        var vehicleRows = rows.Where(row => row.Type == "vehicle" && row.Status == "create").ToList();
        if (vehicleRows.Count > 0)
        {
            var vehicleLimit = await this._vehicleLimits.GetVehicleLimitInfoAsync(organization.OwnerUserId, batch.OrganizationId);
            if (vehicleLimit.MaxVehiclesForUser > 0 &&
                (long)vehicleLimit.CurrentVehicleCount + vehicleRows.Count > vehicleLimit.MaxVehiclesForUser)
            {
                throw new InvalidDataException(
                    $"Превышен лимит техники: сейчас {vehicleLimit.CurrentVehicleCount}, новых {vehicleRows.Count}, лимит {vehicleLimit.MaxVehiclesForUser}.");
            }
        }

        var clientRows = rows.Where(row => row.Type == "client" && row.Status == "create").ToList();
        if (clientRows.Count > 0)
        {
            var clientLimit = Helpers.PlatformDefaults.NormalizeLimit((await this._subscriptions.GetFeatureSnapshotAsync(organization.OwnerUserId, batch.OrganizationId))
                .Features.FirstOrDefault(feature => feature.Code == Helpers.FeatureCodes.WorkspaceClientsLimit)?.IntValue ?? int.MaxValue);
            var clientCount = await this._opencrmContext.OrganizationClients.CountAsync(
                client => client.OrganizationId == batch.OrganizationId, cancellationToken);
            if ((long)clientCount + clientRows.Count > clientLimit)
            {
                throw new InvalidDataException(
                    $"Превышен лимит клиентов: сейчас {clientCount}, новых {clientRows.Count}, лимит {clientLimit}.");
            }
        }
        var clients = clientRows.Select(row => new OrganizationClient
        {
            OrganizationId = batch.OrganizationId,
            Name = row.Name.Trim(),
            PartyTypeCode = "person",
            Phone = row.Phone?.Trim()
        }).ToList();
        foreach (var client in clients)
        {
            client.HistoryEvents.Add(new OrganizationClientHistoryEvent
            {
                OrganizationId = batch.OrganizationId,
                UserId = actorUserId,
                EventType = "imported",
                Title = "Клиент импортирован",
                RelatedEntityType = "crm-import",
                RelatedEntityId = batch.Id
            });
        }

        this._opencrmContext.OrganizationClients.AddRange(clients);
        await this._opencrmContext.SaveChangesAsync(cancellationToken);

        var knownClients = await this._opencrmContext.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == batch.OrganizationId && !client.IsDeleted)
            .Select(client => new { client.Id, client.Phone })
            .ToListAsync(cancellationToken);
        var vehicles = vehicleRows.Select(row => new Vehicle
        {
            OrganizationId = batch.OrganizationId,
            UserId = organization.OwnerUserId,
            OrganizationClientId = knownClients.FirstOrDefault(client =>
                NormalizePhone(client.Phone) == NormalizePhone(row.ClientPhone) &&
                !string.IsNullOrEmpty(NormalizePhone(row.ClientPhone)))?.Id,
            ManualBrandName = row.Brand,
            ManualModelName = row.Model,
            Name = row.Name,
            Vin = row.Vin,
            RegistrationPlate = row.Plate,
            YearIssue = row.Year,
            DateTimeCreation = DateTime.UtcNow
        }).ToList();
        foreach (var vehicle in vehicles)
        {
            vehicle.HistoryEvents.Add(new VehicleHistoryEvent
            {
                OrganizationId = batch.OrganizationId,
                UserId = actorUserId,
                EventType = "imported",
                Title = "Техника импортирована",
                RelatedEntityType = "crm-import",
                RelatedEntityId = batch.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        this._opencrmContext.Vehicles.AddRange(vehicles);
        await this._opencrmContext.SaveChangesAsync(cancellationToken);

        var result = new CrmImportResult
        {
            CreatedClients = clients.Count,
            CreatedVehicles = vehicles.Count,
            SkippedDuplicates = rows.Count(row => row.Status == "duplicate"),
            Errors = rows.Count(row => row.Status == "error"),
            ReportId = batch.Id
        };
        batch.ResultJson = JsonSerializer.Serialize(result);
        batch.ImportedAt = DateTime.UtcNow;
        await this._opencrmContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary> Возвращает содержимое отчёта импорта или результата. </summary>
    public async Task<byte[]?> GetReportAsync(Guid id, bool result, CancellationToken cancellationToken)
    {
        var batch = await this._opencrmContext.CrmImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (batch == null || result && batch.ImportedAt == null)
        {
            return null;
        }

        var rows = JsonSerializer.Deserialize<List<CrmImportRow>>(batch.RowsJson) ?? [];
        var builder = new StringBuilder("\uFEFFНомер;Тип;Имя;Телефон;Телефон клиента;Марка;Модель;VIN;Госномер;Год;Действие;Комментарий\r\n");
        foreach (var row in rows)
        {
            var status = result && row.Status == "create" ? "Создано" : row.Status switch
            {
                "create" => "Будет создано",
                "duplicate" => "Пропуск: дубль",
                _ => "Ошибка"
            };
            var fields = new[]
            {
                row.Number.ToString(), row.Type, row.Name, row.Phone, row.ClientPhone, row.Brand,
                row.Model, row.Vin, row.Plate, row.YearText ?? row.Year?.ToString(), status, row.Message
            };
            builder.AppendLine(string.Join(';', fields.Select(EscapeCsv)));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary> Проверяет строки импорта на корректность и дубликаты. </summary>
    private async Task ValidateRowsAsync(Guid organizationId, List<CrmImportRow> rows, CancellationToken cancellationToken)
    {
        var existingClients = await this._opencrmContext.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted)
            .Select(client => new { client.Name, client.Phone })
            .ToListAsync(cancellationToken);
        var existingVehicles = await this._opencrmContext.Vehicles.AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
            .Select(vehicle => new { vehicle.Vin, vehicle.RegistrationPlate })
            .ToListAsync(cancellationToken);
        var phones = new HashSet<string>();
        var names = new HashSet<string>();
        var vehicleVins = new HashSet<string>();
        var vehiclePlates = new HashSet<string>();

        foreach (var row in rows.OrderBy(item => item.Type == "client" ? 0 : 1))
        {
            row.Status = "create";
            row.Message = string.Empty;
            if (row.Type == "client")
            {
                if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 160 || row.Phone?.Length > 40)
                {
                    Mark(row, "error", "Укажите имя клиента до 160 символов и телефон до 40 символов.");
                    continue;
                }

                var phone = NormalizePhone(row.Phone);
                var name = row.Name.Trim().ToUpperInvariant();
                var isDuplicate = phone.Length > 0
                    ? !phones.Add(phone) || existingClients.Any(client => NormalizePhone(client.Phone) == phone)
                    : !names.Add(name) || existingClients.Any(client => client.Name.ToUpperInvariant() == name);
                if (isDuplicate)
                {
                    Mark(row, "duplicate", "Клиент с таким телефоном или именем уже есть.");
                }

                continue;
            }

            if (row.YearText != null && row.Year == null || row.Year is < 1886 or > 2100 || row.Vin?.Length > 32 || row.Plate?.Length > 24 ||
                row.Brand?.Length > 100 || row.Model?.Length > 100)
            {
                Mark(row, "error", "Проверьте год, VIN, госномер, марку и модель.");
                continue;
            }

            var vin = NormalizeKey(row.Vin);
            var plate = NormalizeKey(row.Plate);
            if (vin.Length == 0 && plate.Length == 0)
            {
                Mark(row, "error", "Для автомобиля требуется VIN или госномер.");
                continue;
            }

            if (vin.Length > 0 && vehicleVins.Contains(vin) ||
                plate.Length > 0 && vehiclePlates.Contains(plate) ||
                existingVehicles.Any(vehicle =>
                    vin.Length > 0 && NormalizeKey(vehicle.Vin) == vin ||
                    plate.Length > 0 && NormalizeKey(vehicle.RegistrationPlate) == plate))
            {
                Mark(row, "duplicate", "Автомобиль с таким VIN или госномером уже есть.");
                continue;
            }

            if (vin.Length > 0)
            {
                vehicleVins.Add(vin);
            }

            if (plate.Length > 0)
            {
                vehiclePlates.Add(plate);
            }

            var clientPhone = NormalizePhone(row.ClientPhone);
            if (clientPhone.Length > 0 && existingClients.Count(client => NormalizePhone(client.Phone) == clientPhone) +
                rows.Count(client => client.Type == "client" && client.Status == "create" && NormalizePhone(client.Phone) == clientPhone) != 1)
            {
                Mark(row, "error", "Телефон клиента не соответствует ровно одному клиенту.");
            }
        }
    }

    /// <summary> Создаёт объект предпросмотра из пакета и строк. </summary>
    private static CrmImportPreview CreatePreview(CrmImportBatch batch, IReadOnlyList<CrmImportRow> rows, bool alreadyImported) => new()
    {
        Id = batch.Id,
        FileName = batch.FileName,
        AlreadyImported = alreadyImported,
        Rows = rows,
        Summary = new CrmImportSummary
        {
            Clients = rows.Count(row => row.Type == "client"),
            Vehicles = rows.Count(row => row.Type == "vehicle"),
            Create = rows.Count(row => row.Status == "create"),
            Duplicate = rows.Count(row => row.Status == "duplicate"),
            Error = rows.Count(row => row.Status == "error")
        }
    };

    /// <summary> Помечает строку статусом и сообщением. </summary>
    private static void Mark(CrmImportRow row, string status, string message)
    {
        row.Status = status;
        row.Message = message;
    }

    /// <summary> Нормализует телефон для сравнения. </summary>
    private static string NormalizePhone(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary> Нормализует значение ключа для сравнения. </summary>
    private static string NormalizeKey(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary> Экранирует значение для CSV. </summary>
    private static string EscapeCsv(string? value)
    {
        var safeValue = value ?? string.Empty;
        if (safeValue.Length > 0 && "=+-@".Contains(safeValue[0]))
        {
            safeValue = "'" + safeValue;
        }

        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }
}
