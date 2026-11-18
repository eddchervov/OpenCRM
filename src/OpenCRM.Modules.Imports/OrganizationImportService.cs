using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Imports;

/// <summary> Сохраняет подготовку импорта и атомарно применяет согласованный результат. </summary>
public sealed partial class OrganizationImportService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IVehicleLimitQueryService vehicleLimits,
    IImportBusinessRecordProcessor? businessRecords = null) : IOrganizationImportService, IOrganizationImportBatchService
{
    /// <summary> Проверяет права и действующий доступ к обоим импортируемым разделам. </summary>
    public async Task<bool> CanImportAsync(string userId, Guid organizationId)
    {
        try
        {
            await this.EnsureAccessAsync(userId, organizationId);

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary> Возвращает последние загрузки без содержимого файлов. </summary>
    public async Task<IReadOnlyList<ImportDraftDto>> ListAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var canReadOthers = await this.CanReadOtherBatchesAsync(userId, organizationId);

        return await context.Set<OrganizationImportDraft>().AsNoTracking()
            .Where(draft => draft.OrganizationId == organizationId)
            .Where(draft => draft.UserId == userId || canReadOthers)
            .OrderByDescending(draft => draft.UpdatedAt).Take(50)
            .Select(draft => new ImportDraftDto
            {
                Id = draft.Id,
                FileName = draft.FileName,
                Revision = draft.Revision,
                UpdatedAt = draft.UpdatedAt,
                ImportedAt = draft.ImportedAt,
                StorageVersion = draft.StorageVersion,
                State = draft.State,
                TotalRows = draft.TotalRows,
                ProcessedRows = draft.ProcessedRows
            }).ToListAsync(cancellationToken);
    }

    /// <summary> Открывает черновик только в разрешённой организации. </summary>
    public async Task<ImportDraftDto> GetAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);

        return draft.StorageVersion == 2
            ? await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken)
            : ToDto(draft);
    }

    /// <summary> Загружает исходные таблицы, не создавая клиентов и технику. </summary>
    public async Task<ImportDraftDto> CreateAsync(
        string userId, Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var tables = ImportFileParser.Parse(content, fileName);
        var data = new ImportDraftData { Tables = tables, Mappings = ImportMappingBuilder.Suggest(tables) };
        var draft = new OrganizationImportDraft
        {
            OrganizationId = organizationId,
            UserId = userId,
            FileName = Path.GetFileName(fileName),
            PayloadJson = JsonSerializer.Serialize(data)
        };

        if (draft.FileName.Length > 260)
        {
            throw new InvalidDataException("Имя файла должно быть не длиннее 260 символов.");
        }

        context.Set<OrganizationImportDraft>().Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        return ToDto(draft);
    }

    /// <summary> Пересоздаёт будущие записи по выбранному соответствию столбцов. </summary>
    public async Task<ImportDraftDto> MapAsync(
        string userId, Guid organizationId, Guid id, int revision, List<ImportTableMapping> mappings, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        if (draft.StorageVersion == 2)
        {
            return await this.QueueMappingAsync(userId, organizationId, id, revision, mappings, cancellationToken);
        }
        if (mappings.Any(mapping => mapping.Kind is "order" or "order-line" or "payment" or "history"))
        {
            throw new InvalidDataException("Для заказов и оплат создайте новый фоновый пакет импорта.");
        }
        EnsureEditable(draft, revision);
        var data = ReadData(draft);
        data.Rows = ImportMappingBuilder.Build(data.Tables, mappings);
        data.Mappings = mappings;
        await this.ValidateAsync(userId, organizationId, data.Rows, cancellationToken);
        await this.SaveDraftAsync(draft, data, cancellationToken);

        return ToDto(draft);
    }

    /// <summary> Сохраняет только редактируемые поля строк с проверкой происхождения. </summary>
    public async Task<ImportDraftDto> SaveAsync(
        string userId, Guid organizationId, Guid id, int revision, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        if (draft.StorageVersion == 2)
        {
            return await this.PatchRowsAsync(userId, organizationId, id, revision, rows, cancellationToken);
        }
        EnsureEditable(draft, revision);
        var data = ReadData(draft);
        if (rows == null || rows.Count != data.Rows.Count || rows.Any(row => row == null) ||
            rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            throw new InvalidDataException("Состав строк изменился. Откройте черновик заново.");
        }

        var edits = rows.ToDictionary(row => row.Id);
        foreach (var original in data.Rows)
        {
            if (!edits.TryGetValue(original.Id, out var edit) || edit.Kind != original.Kind ||
                edit.TableId != original.TableId || edit.SourceRow != original.SourceRow)
            {
                throw new InvalidDataException("Нельзя изменить происхождение и тип строки. Используйте настройку столбцов.");
            }

            original.Excluded = edit.Excluded;
            original.Name = Clean(edit.Name) ?? string.Empty;
            original.Phone = Clean(edit.Phone);
            original.Comment = Clean(edit.Comment);
            original.ExternalId = Clean(edit.ExternalId);
            original.ClientExternalId = Clean(edit.ClientExternalId);
            original.ClientPhone = Clean(edit.ClientPhone);
            original.Brand = Clean(edit.Brand);
            original.Model = Clean(edit.Model);
            original.Vin = Clean(edit.Vin);
            original.Plate = Clean(edit.Plate);
            original.RegistrationCountryCode = Clean(edit.RegistrationCountryCode);
            original.Year = Clean(edit.Year);
            original.ExistingClientId = edit.ExistingClientId;
            original.CreateSeparateClient = edit.CreateSeparateClient && !edit.ExistingClientId.HasValue;
        }

        await this.ValidateAsync(userId, organizationId, data.Rows, cancellationToken);
        await this.SaveDraftAsync(draft, data, cancellationToken);

        return ToDto(draft);
    }

    /// <summary> Подтверждает неизменившийся план в транзакции и возвращает результат при повторе запроса. </summary>
    public async Task<ImportDraftDto> ConfirmAsync(
        string userId, Guid organizationId, Guid id, int revision, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var existing = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, existing);
        if (existing.StorageVersion == 2)
        {
            return await this.StartAsync(userId, organizationId, id, revision, false, cancellationToken);
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);

            var draft = await this.LoadAsync(organizationId, id, cancellationToken);
            if (draft.ImportedAt.HasValue)
            {
                return ToDto(draft);
            }

            EnsureEditable(draft, revision);
            var data = ReadData(draft);
            if (data.Rows.Count == 0 || data.Rows.All(row => row.Excluded))
            {
                throw new InvalidDataException("Нет строк для импорта. Настройте столбцы и проверьте результат.");
            }

            if (data.Rows.Any(row => row.Status is "error" or "conflict"))
            {
                throw new InvalidDataException("Исправьте ошибки и неоднозначные совпадения перед импортом.");
            }

            var reviewed = JsonSerializer.Serialize(data.Rows);
            await this.EnsureAccessAsync(userId, organizationId);
            await this.ValidateAsync(userId, organizationId, data.Rows, cancellationToken);
            if (reviewed != JsonSerializer.Serialize(data.Rows))
            {
                throw new InvalidDataException("Данные организации изменились. Сохраните и проверьте черновик ещё раз.");
            }

            var result = this.ApplyRows(userId, organizationId, draft.Id, data.Rows);
            draft.ResultJson = JsonSerializer.Serialize(result);
            draft.ImportedAt = DateTime.UtcNow;
            await this.SaveDraftAsync(draft, data, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToDto(draft);
        });
    }

    /// <summary> Формирует выгружаемый отчёт с безопасными текстовыми ячейками. </summary>
    public async Task<byte[]> GetReportAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var dto = await this.GetAsync(userId, organizationId, id, cancellationToken);
        if (dto.StorageVersion == 2)
        {
            dto.Data.Rows = await this.ReadCandidatesAsync(id, cancellationToken);
        }

        var report = new StringBuilder("\uFEFFТаблица;Строка;Тип;Имя;Телефон;Исходный ID;ID клиента;Марка;Модель;VIN;Страна регистрации;Госномер;Год;Клиент;Действие;Пояснение;ID результата;Применено;Внешний ID авто;Внешний ID заказа;Исходная дата;Статус заказа;Тип заказа;Количество;Цена единицы;Сумма;Скидка;Способ оплаты;Дата отмены;Исходный автор;Тип события;Комментарий клиента;Комментарий заказа;Описание строки;Комментарий события или оплаты;Историческое имя покупателя или плательщика\r\n");
        foreach (var row in dto.Data.Rows)
        {
            var status = row.Status switch
            {
                "create" => dto.ImportedAt.HasValue || row.AppliedAt.HasValue ? "Создано" : "Будет создано",
                "link" => "Использовать существующего клиента",
                "duplicate" => "Пропустить дубль",
                "skip" => "Исключено",
                "conflict" => "Требуется выбор",
                _ => "Ошибка"
            };

            var values = new[]
            {
                row.TableId, row.SourceRow.ToString(CultureInfo.InvariantCulture), row.Kind,
                row.Name, row.Phone, row.ExternalId, row.ClientExternalId, row.Brand, row.Model, row.Vin,
                row.RegistrationCountryCode, row.Plate,
                row.Year, row.LinkedClientName, status, row.Message, row.TargetId?.ToString(), row.AppliedAt?.ToString("O", CultureInfo.InvariantCulture),
                row.VehicleExternalId, row.OrderExternalId, row.OccurredAt, row.OrderStatus, row.OrderType, row.Quantity, row.UnitPrice,
                row.Amount, row.DiscountAmount, row.PaymentMethod, row.CancelledAt, row.ActorName, row.EventType, row.Comment,
                row.OrderComment, row.DetailDescription, row.HistoryComment, row.PartyName
            };

            report.AppendLine(string.Join(';', values.Select(EscapeCsv)));
        }

        return Encoding.UTF8.GetBytes(report.ToString());
    }

    private async Task EnsureAccessAsync(string userId, Guid organizationId)
    {
        string[] requiredPermissions =
        [
            OrganizationPermissionCodes.WorkspaceProfileEdit, OrganizationPermissionCodes.ClientsView,
            OrganizationPermissionCodes.ClientsCreate, OrganizationPermissionCodes.VehiclesView,
            OrganizationPermissionCodes.VehiclesCreate, OrganizationPermissionCodes.ClientsVehiclesManage
        ];

        foreach (var permission in requiredPermissions)
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, permission);
        }

        var requiresPaidImport = await context.Organizations.AsNoTracking().AnyAsync(organization =>
            organization.Id == organizationId
            && (organization.PrimaryScenarioCode == OrganizationTypeCodes.Detailing
                || organization.PrimaryScenarioCode == OrganizationTypeCodes.ServiceStation));

        if (requiresPaidImport && !await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.DataImport))
        {
            throw new InvalidOperationException("Импорт данных доступен в PRO.");
        }

        if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule) ||
            !await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.VehiclesModule))
        {
            throw new UnauthorizedAccessException("Импорт требует доступа к клиентам и технике организации.");
        }
    }

    private async Task<bool> CanReadOtherBatchesAsync(string userId, Guid organizationId)
    {
        try
        {
            await this.EnsureFullBatchAccessAsync(userId, organizationId);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<OrganizationImportDraft> LoadAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        await context.Set<OrganizationImportDraft>()
            .SingleOrDefaultAsync(draft => draft.Id == id && draft.OrganizationId == organizationId, cancellationToken)
        ?? throw new KeyNotFoundException("Черновик не найден в выбранной организации.");

    private async Task ValidateAsync(string userId, Guid organizationId, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        var clients = await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted)
            .OrderBy(client => client.Id)
            .Select(client => new ImportClientMatch { Id = client.Id, Name = client.Name, Phone = client.Phone })
            .ToListAsync(cancellationToken);

        var vehicles = await context.Vehicles.AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
            .Select(vehicle => new { vehicle.Vin, vehicle.RegistrationPlate, vehicle.RegistrationCountryCode }).ToListAsync(cancellationToken);

        ImportCandidateValidator.ValidateWithCountries(rows, clients, vehicles.Select(vehicle =>
            (vehicle.Vin, vehicle.RegistrationPlate, vehicle.RegistrationCountryCode)).ToList());
        var newClients = rows.Where(row => row.Kind == "client" && row.Status == "create").ToList();
        if (newClients.Count > 0)
        {
            var clientLimit = Helpers.PlatformDefaults.NormalizeLimit((await subscriptions.GetFeatureSnapshotAsync(userId, organizationId)).Features
                .FirstOrDefault(feature => feature.Code == FeatureCodes.WorkspaceClientsLimit)?.IntValue ?? int.MaxValue);
            var clientCount = await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId, cancellationToken);
            if ((long)clientCount + newClients.Count > clientLimit)
            {
                foreach (var row in newClients)
                {
                    row.Status = "error";
                    row.Message = $"Превышен лимит клиентов: сейчас {clientCount}, новых {newClients.Count}, лимит {clientLimit}.";
                    row.MessageCode = ImportRowMessageCodes.ClientLimitExceeded;
                    row.MessageDetails = null;
                    row.MessageParameters = new Dictionary<string, string>
                    {
                        ["current"] = clientCount.ToString(CultureInfo.InvariantCulture),
                        ["newCount"] = newClients.Count.ToString(CultureInfo.InvariantCulture),
                        ["limit"] = clientLimit.ToString(CultureInfo.InvariantCulture)
                    };
                }
            }
        }

        var limit = await vehicleLimits.GetVehicleLimitInfoAsync(userId, organizationId);
        var newVehicles = rows.Where(row => row.Kind == "vehicle" && row.Status == "create").ToList();
        if (limit.MaxVehiclesForUser > 0 &&
            (long)limit.CurrentVehicleCount + newVehicles.Count > limit.MaxVehiclesForUser)
        {
            foreach (var row in newVehicles)
            {
                row.Status = "error";
                row.Message = $"Превышен лимит техники: сейчас {limit.CurrentVehicleCount}, новых {newVehicles.Count}, лимит {limit.MaxVehiclesForUser}.";
                row.MessageCode = ImportRowMessageCodes.VehicleLimitExceeded;
                row.MessageDetails = null;
                row.MessageParameters = new Dictionary<string, string>
                {
                    ["current"] = limit.CurrentVehicleCount.ToString(CultureInfo.InvariantCulture),
                    ["newCount"] = newVehicles.Count.ToString(CultureInfo.InvariantCulture),
                    ["limit"] = limit.MaxVehiclesForUser.ToString(CultureInfo.InvariantCulture)
                };
            }
        }
    }

    private ImportResultDto ApplyRows(string userId, Guid organizationId, Guid draftId, List<ImportCandidate> rows)
    {
        var result = new ImportResultDto
        {
            LinkedClients = rows.Count(row => row.Kind == "client" && row.Status == "link"),
            Skipped = rows.Count(row => row.Status is "skip" or "duplicate")
        };

        var clientIds = new Dictionary<string, Guid>();
        foreach (var row in rows.Where(row => row.Kind == "client" && row.Status == "create"))
        {
            var client = new OrganizationClient { OrganizationId = organizationId, Name = row.Name, Phone = row.Phone, Comment = row.Comment };
            client.HistoryEvents.Add(new OrganizationClientHistoryEvent
            {
                OrganizationId = organizationId,
                UserId = userId,
                EventType = "imported",
                Title = "Клиент импортирован",
                RelatedEntityType = "organization-import",
                RelatedEntityId = draftId
            });

            context.OrganizationClients.Add(client);
            row.Message = "Создано.";
            row.MessageCode = ImportRowMessageCodes.StatusCreated;
            row.MessageDetails = null;
            row.MessageParameters = null;
            clientIds.Add(row.Id, client.Id);
            result.ClientIds.Add(client.Id);
        }

        foreach (var row in rows.Where(row => row.Kind == "vehicle" && row.Status == "create"))
        {
            var vehicle = new Vehicle
            {
                OrganizationId = organizationId,
                UserId = userId,
                OrganizationClientId = row.ResolvedClientId ?? (row.ResolvedClientRowId == null ? null : clientIds[row.ResolvedClientRowId]),
                Name = Clean(row.Name),
                ManualBrandName = row.Brand,
                ManualModelName = row.Model,
                Vin = row.Vin,
                RegistrationPlate = row.Plate,
                RegistrationCountryCode = InternationalRegistrationPlateRules.NormalizeCountryCode(row.RegistrationCountryCode),
                YearIssue = row.Year == null ? null : int.Parse(row.Year, CultureInfo.InvariantCulture),
                DateTimeCreation = DateTime.UtcNow
            };

            vehicle.HistoryEvents.Add(new VehicleHistoryEvent
            {
                OrganizationId = organizationId,
                UserId = userId,
                EventType = "imported",
                Title = "Техника импортирована",
                RelatedEntityType = "organization-import",
                RelatedEntityId = draftId,
                CreatedAt = DateTime.UtcNow
            });

            context.Vehicles.Add(vehicle);
            row.Message = "Создано.";
            row.MessageCode = ImportRowMessageCodes.StatusCreated;
            row.MessageDetails = null;
            row.MessageParameters = null;
            result.VehicleIds.Add(vehicle.Id);
        }

        result.CreatedClients = result.ClientIds.Count;
        result.CreatedVehicles = result.VehicleIds.Count;

        return result;
    }

    private async Task SaveDraftAsync(OrganizationImportDraft draft, ImportDraftData data, CancellationToken cancellationToken)
    {
        draft.Revision++;
        draft.UpdatedAt = DateTime.UtcNow;
        draft.PayloadJson = JsonSerializer.Serialize(data);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureEditable(OrganizationImportDraft draft, int revision)
    {
        if (draft.StorageVersion == 2 && draft.State != "ready")
        {
            throw new InvalidDataException("Дождитесь завершения фоновой проверки. Во время переноса редактирование недоступно.");
        }

        if (draft.ImportedAt.HasValue)
        {
            throw new InvalidDataException("Импорт завершён. Его данные доступны только для просмотра.");
        }

        if (draft.Revision != revision)
        {
            throw new InvalidDataException("Черновик изменён в другой вкладке. Откройте его заново, чтобы не потерять чужие правки.");
        }
    }

    private static ImportDraftData ReadData(OrganizationImportDraft draft) =>
        JsonSerializer.Deserialize<ImportDraftData>(draft.PayloadJson) ?? throw new InvalidDataException("Черновик повреждён.");

    private static ImportDraftDto ToDto(OrganizationImportDraft draft) => new()
    {
        Id = draft.Id,
        FileName = draft.FileName,
        Revision = draft.Revision,
        UpdatedAt = draft.UpdatedAt,
        ImportedAt = draft.ImportedAt,
        Data = ReadData(draft),
        Result = draft.ResultJson == null ? null : JsonSerializer.Deserialize<ImportResultDto>(draft.ResultJson)
    };

    private static string? Clean(string? value)
    {
        if (value?.Contains('\0') == true)
        {
            throw new InvalidDataException("В ячейке найден недопустимый нулевой символ.");
        }

        if (value?.Length > 10000)
        {
            throw new InvalidDataException("Значение ячейки не должно превышать 10000 символов.");
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
