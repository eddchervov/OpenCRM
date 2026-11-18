using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Imports;

public sealed partial class OrganizationImportService
{
    private static readonly string[] s_pendingStates = ["upload-queued", "mapping-queued", "validation-queued", "import-queued", "importing"];
    private static readonly HashSet<string> s_editableFields =
    [
        "Name", "Excluded", "ExternalId", "ClientExternalId", "ClientPhone", "Phone", "Comment", "Brand", "Model", "Vin", "Plate", "RegistrationCountryCode", "Year",
        "ExistingClientId", "CreateSeparateClient", "PartyName", "VehicleExternalId", "OrderExternalId", "OccurredAt", "OrderStatus", "OrderType",
        "DetailDescription", "Quantity", "UnitPrice", "Amount", "DiscountAmount", "PaymentMethod", "CancelledAt", "ActorName", "EventType", "OrderComment", "HistoryComment"
    ];

    public async Task<ImportDraftDto> UploadAsync(
        string userId, Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        if (content.Length is 0 or > 50 * 1024 * 1024)
        {
            throw new InvalidDataException("Выберите непустой файл размером до 50 МБ.");
        }

        var name = Path.GetFileName(fileName);
        if (name.Length > 260 || Path.GetExtension(name).ToLowerInvariant() is not (".csv" or ".xlsx" or ".sql" or ".zip"))
        {
            throw new InvalidDataException("Поддерживаются CSV, XLSX, ZIP с CSV и ограниченные текстовые SQL-выгрузки; имя до 260 символов.");
        }

        var draft = new OrganizationImportDraft
        {
            OrganizationId = organizationId,
            UserId = userId,
            FileName = name,
            StorageVersion = 2,
            State = "upload-queued",
            RequestedBy = userId,
            PayloadJson = JsonSerializer.Serialize(new ImportDraftData())
        };

        context.Add(draft);
        context.Add(new OrganizationImportFile { DraftId = draft.Id, Content = content });
        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, draft.Id, 1, 50, null, null, null, cancellationToken);
    }

    public async Task<ImportDraftDto> GetPageAsync(
        string userId, Guid organizationId, Guid id, int page, int pageSize,
        string? kind, string? status, string? search, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        if (draft.StorageVersion != 2)
        {
            return ToDto(draft);
        }

        var data = ReadData(draft);
        foreach (var table in data.Tables)
        {
            var source = await context.Set<OrganizationImportSourceRow>().AsNoTracking()
                .Where(row => row.DraftId == id && row.TableId == table.Id).OrderBy(row => row.Number)
                .Take(20).ToListAsync(cancellationToken);
            table.Rows = source.Select(row => new ImportSourceRow
            {
                Number = row.Number,
                Values = JsonSerializer.Deserialize<List<string>>(row.ValuesJson)!
            }).ToList();
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var all = context.Set<OrganizationImportRow>().AsNoTracking().Where(row => row.DraftId == id);
        var counts = await all.GroupBy(row => row.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, cancellationToken);
        var filtered = all;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            filtered = filtered.Where(row => row.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filtered = filtered.Where(row => row.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            if (term.Length > 200)
            {
                throw new InvalidDataException("Поиск ограничен 200 символами.");
            }

            filtered = filtered.Where(row => row.SearchText.Contains(term));
        }

        var filteredRows = await filtered.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (filteredRows + pageSize - 1) / pageSize));
        var payloads = await filtered.OrderBy(row => row.Position).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => row.PayloadJson).ToListAsync(cancellationToken);
        data.Rows = payloads.Select(DeserializeCandidate).ToList();

        return new ImportDraftDto
        {
            Id = draft.Id,
            SourceKey = draft.SourceKey,
            FileName = draft.FileName,
            Revision = draft.Revision,
            StorageVersion = 2,
            UpdatedAt = draft.UpdatedAt,
            ImportedAt = draft.ImportedAt,
            State = draft.State,
            Error = draft.Error,
            TotalRows = draft.TotalRows,
            ProcessedRows = draft.ProcessedRows,
            FilteredRows = filteredRows,
            Page = page,
            PageSize = pageSize,
            Counts = counts,
            Data = data,
            Result = draft.ResultJson == null ? null : JsonSerializer.Deserialize<ImportResultDto>(draft.ResultJson)
        };
    }

    public async Task<ImportDraftDto> QueueMappingAsync(
        string userId, Guid organizationId, Guid id, int revision, List<ImportTableMapping> mappings, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        EnsureBatchEditable(draft, revision);
        if (draft.ProcessedRows > 0)
        {
            throw new InvalidDataException("После начала переноса нельзя перестраивать сопоставление.");
        }

        var data = ReadData(draft);
        ImportMappingBuilder.Build(data.Tables, mappings);
        data.Mappings = mappings;
        draft.PayloadJson = JsonSerializer.Serialize(data);
        this.Queue(draft, "mapping-queued", userId);
        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
    }

    public async Task<ImportDraftDto> PatchRowsAsync(
        string userId, Guid organizationId, Guid id, int revision, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        EnsureBatchEditable(draft, revision);
        if (rows == null || rows.Count is 0 or > 100 || rows.Any(row => row == null) || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            throw new InvalidDataException("За одно сохранение можно изменить от 1 до 100 различных строк.");
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var entities = await context.Set<OrganizationImportRow>().Where(row => row.DraftId == id && ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        if (entities.Count != rows.Count || entities.Values.Any(row => row.AppliedAt.HasValue))
        {
            throw new InvalidDataException("Строки не найдены или уже применены. Откройте актуальную версию.");
        }

        foreach (var edit in rows)
        {
            var entity = entities[edit.Id];
            var original = DeserializeCandidate(entity.PayloadJson);
            if (original.Kind != edit.Kind || original.TableId != edit.TableId || original.SourceRow != edit.SourceRow)
            {
                throw new InvalidDataException("Нельзя изменять происхождение строки.");
            }

            foreach (var property in typeof(ImportCandidate).GetProperties().Where(property => s_editableFields.Contains(property.Name)))
            {
                var value = property.GetValue(edit);
                if (value is string text)
                {
                    value = Clean(text);
                }

                property.SetValue(original, value ?? (property.Name == "Name" ? string.Empty : null));
            }

            UpdateRow(entity, original);
        }

        this.Queue(draft, "validation-queued", userId);
        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
    }

    public async Task<ImportDraftDto> StartAsync(
        string userId, Guid organizationId, Guid id, int revision, bool resume, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        if (draft.ImportedAt.HasValue || draft.State is "import-queued" or "importing")
        {
            return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
        }

        if (draft.Revision != revision)
        {
            throw new InvalidDataException("Версия изменилась. Откройте пакет заново.");
        }

        if (resume && draft.State == "failed" && draft.ResumeState is "import-queued" or "importing")
        {
            draft.State = "paused";
        }
        if (resume && draft.State == "failed" && draft.ResumeState is "upload-queued" or "mapping-queued" or "validation-queued")
        {
            this.Queue(draft, draft.ResumeState, userId);
        }
        else
        {
            EnsureBatchEditable(draft, revision);
            if (draft.TotalRows == 0 || await context.Set<OrganizationImportRow>().AnyAsync(
                row => row.DraftId == id && (row.Status == "error" || row.Status == "conflict"), cancellationToken))
            {
                throw new InvalidDataException("Проверьте непустой пакет, исправьте ошибки и конфликты.");
            }
            if (draft.ProcessedRows == 0 && !await context.Set<OrganizationImportRow>().AnyAsync(
                row => row.DraftId == id && (row.Status == "create" || row.Status == "link" || row.Status == "duplicate"), cancellationToken))
            {
                throw new InvalidDataException("Все строки исключены или являются дублями. Нет данных для переноса.");
            }

            await this.EnsureBusinessAccessAsync(userId, organizationId, id, cancellationToken);
            await this.EnsureHistoricalSourceAsync(draft, cancellationToken);
            this.Queue(draft, "import-queued", userId);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
    }

    public async Task<ImportDraftDto> PauseAsync(
        string userId, Guid organizationId, Guid id, int revision, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        if (draft.Revision != revision)
        {
            throw new InvalidDataException("Прогресс изменился. Обновите страницу.");
        }

        if (draft.State is not ("import-queued" or "importing"))
        {
            throw new InvalidDataException("Перенос сейчас не выполняется.");
        }

        draft.State = "paused";
        draft.Revision++;
        draft.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var id = await context.Set<OrganizationImportDraft>().AsNoTracking()
            .Where(draft => draft.StorageVersion == 2 && s_pendingStates.Contains(draft.State))
            .OrderBy(draft => draft.UpdatedAt).Select(draft => (Guid?)draft.Id).FirstOrDefaultAsync(cancellationToken);
        if (!id.HasValue)
        {
            return;
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        var lockKey = BitConverter.ToInt64(id.Value.ToByteArray(), 0);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), true))
        {
            await context.Database.CloseConnectionAsync();
            return;
        }
        string? currentState = null;
        try
        {
            context.ChangeTracker.Clear();
            var draft = await context.Set<OrganizationImportDraft>().SingleAsync(draft => draft.Id == id, cancellationToken);
            currentState = draft.State;
            await this.EnsureAccessAsync(draft.RequestedBy ?? draft.UserId, draft.OrganizationId);
            if (currentState == "upload-queued")
            {
                await this.ParseBatchAsync(draft, cancellationToken);
            }
            else if (currentState is "mapping-queued" or "validation-queued")
            {
                await this.PrepareBatchAsync(draft, cancellationToken);
            }
            else if (currentState is "import-queued" or "importing")
            {
                await this.ApplyBatchAsync(draft, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            var draft = await context.Set<OrganizationImportDraft>().SingleAsync(draft => draft.Id == id, cancellationToken);
            if (!s_pendingStates.Contains(draft.State))
            {
                return;
            }

            draft.ResumeState = currentState;
            draft.State = draft.ProcessedRows > 0 ? "paused" : "failed";
            if (currentState == "mapping-queued" && exception is InvalidDataException)
            {
                await context.Set<OrganizationImportRow>().Where(row => row.DraftId == draft.Id).ExecuteDeleteAsync(cancellationToken);
                draft.TotalRows = 0;
                draft.State = "ready";
            }
            var isExpected = exception is InvalidDataException or InvalidOperationException or UnauthorizedAccessException;
            draft.Error = isExpected ? exception.Message[..Math.Min(exception.Message.Length, 2000)]
                : "Фоновая обработка остановлена из-за сбоя. Сохранённый прогресс доступен; повторите операцию или обратитесь в поддержку.";
            draft.Revision++;
            await context.SaveChangesAsync(cancellationToken);
            if (!isExpected)
            {
                throw;
            }
        }
        finally
        {
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            await command.ExecuteScalarAsync(CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task ParseBatchAsync(OrganizationImportDraft draft, CancellationToken cancellationToken)
    {
        var content = await context.Set<OrganizationImportFile>().Where(file => file.DraftId == draft.Id)
            .Select(file => file.Content).SingleAsync(cancellationToken);
        var tables = Path.GetExtension(draft.FileName).Equals(".sql", StringComparison.OrdinalIgnoreCase)
            ? ImportSqlDataParser.Parse(content)
            : ImportFileParser.Parse(content, draft.FileName, large: true);
        await context.Set<OrganizationImportSourceRow>().Where(row => row.DraftId == draft.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var table in tables)
        {
            foreach (var chunk in table.Rows.Chunk(500))
            {
                var entities = chunk.Select(row => new OrganizationImportSourceRow
                {
                    DraftId = draft.Id,
                    TableId = table.Id,
                    Number = row.Number,
                    ValuesJson = JsonSerializer.Serialize(row.Values)
                }).ToList();
                context.AddRange(entities);
                await context.SaveChangesAsync(cancellationToken);
                foreach (var entity in entities)
                {
                    context.Entry(entity).State = EntityState.Detached;
                }
            }
        }

        var data = new ImportDraftData { Tables = tables, Mappings = ImportMappingBuilder.Suggest(tables) };
        foreach (var table in data.Tables)
        {
            table.Rows = [];
        }

        draft.PayloadJson = JsonSerializer.Serialize(data);
        draft.State = "ready";
        draft.Error = null;
        draft.Revision++;
        draft.UpdatedAt = DateTime.UtcNow;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Set<OrganizationImportFile>().Where(file => file.DraftId == draft.Id).ExecuteDeleteAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task PrepareBatchAsync(OrganizationImportDraft draft, CancellationToken cancellationToken)
    {
        var rows = await this.ReadCandidatesAsync(draft.Id, cancellationToken);
        if (draft.State == "mapping-queued")
        {
            var data = ReadData(draft);
            foreach (var table in data.Tables)
            {
                var source = await context.Set<OrganizationImportSourceRow>().AsNoTracking()
                    .Where(row => row.DraftId == draft.Id && row.TableId == table.Id).OrderBy(row => row.Number).ToListAsync(cancellationToken);
                table.Rows = source.Select(row => new ImportSourceRow
                {
                    Number = row.Number,
                    Values = JsonSerializer.Deserialize<List<string>>(row.ValuesJson)!
                }).ToList();
            }

            rows = ImportMappingBuilder.Build(data.Tables, data.Mappings);
        }

        await this.ValidateBatchRowsAsync(draft, rows, cancellationToken);
        var metadata = ReadData(draft);
        metadata.Rows = [];
        metadata.Reconciliation = ImportReconciliationBuilder.Build(rows);
        draft.PayloadJson = JsonSerializer.Serialize(metadata);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            if (draft.State == "mapping-queued")
            {
                await context.Set<OrganizationImportRow>().Where(row => row.DraftId == draft.Id).ExecuteDeleteAsync(cancellationToken);
                foreach (var chunk in rows.Select((row, position) => CreateRow(draft.Id, row, position)).Chunk(500))
                {
                    context.AddRange(chunk);
                    await context.SaveChangesAsync(cancellationToken);
                    foreach (var entity in chunk)
                    {
                        context.Entry(entity).State = EntityState.Detached;
                    }
                }
            }
            else
            {
                await this.PersistValidationAsync(draft.Id, rows, cancellationToken);
            }

            draft.TotalRows = rows.Count;
            draft.State = draft.ProcessedRows > 0 ? "paused" : "ready";
            draft.Error = null;
            draft.Revision++;
            draft.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task<List<ImportCandidate>> ReadCandidatesAsync(Guid id, CancellationToken cancellationToken)
    {
        var payloads = await context.Set<OrganizationImportRow>().AsNoTracking().Where(row => row.DraftId == id)
            .OrderBy(row => row.Position).Select(row => row.PayloadJson).ToListAsync(cancellationToken);

        return payloads.Select(DeserializeCandidate).ToList();
    }

    private static ImportCandidate DeserializeCandidate(string payload) =>
        JsonSerializer.Deserialize<ImportCandidate>(payload) ?? throw new InvalidDataException("Повреждена строка подготовки.");

    private static OrganizationImportRow CreateRow(Guid draftId, ImportCandidate row, int position)
    {
        var entity = new OrganizationImportRow { DraftId = draftId, Id = row.Id, Position = position };
        UpdateRow(entity, row);

        return entity;
    }

    private static void UpdateRow(OrganizationImportRow entity, ImportCandidate row)
    {
        entity.Kind = row.Kind;
        entity.Status = row.Status;
        entity.ExternalId = row.ExternalId?.Length <= 100 ? ExternalKey(row.ExternalId) : null;
        entity.ParentExternalId = row.OrderExternalId?.Length <= 100 ? ExternalKey(row.OrderExternalId) : null;
        entity.PayloadJson = JsonSerializer.Serialize(row);
        entity.SearchText = string.Join(' ', typeof(ImportCandidate).GetProperties()
            .Where(property => property.PropertyType == typeof(string)).Select(property => property.GetValue(row))).ToLowerInvariant();
    }

    private void Queue(OrganizationImportDraft draft, string state, string userId)
    {
        draft.State = state;
        draft.RequestedBy = userId;
        draft.Error = null;
        draft.Revision++;
        draft.UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureBatchEditable(OrganizationImportDraft draft, int revision)
    {
        if (draft.StorageVersion != 2 || draft.Revision != revision)
        {
            throw new InvalidDataException("Версия пакета изменилась. Откройте его заново.");
        }

        if (draft.State is not ("ready" or "paused") || draft.ImportedAt.HasValue)
        {
            throw new InvalidDataException("Пакет занят фоновым заданием или уже завершён.");
        }
    }

    private static string? ExternalKey(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private async Task EnsureDraftReadAccessAsync(string userId, OrganizationImportDraft draft)
    {
        if (draft.UserId == userId)
        {
            return;
        }

        await this.EnsureFullBatchAccessAsync(userId, draft.OrganizationId);
    }

    private async Task EnsureFullBatchAccessAsync(string userId, Guid organizationId)
    {
        foreach (var permission in new[] { OrganizationPermissionCodes.MaintenanceView, OrganizationPermissionCodes.MaintenanceCreate,
            OrganizationPermissionCodes.FinanceView, OrganizationPermissionCodes.FinanceManage })
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, permission);
        }
        if (!await subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.MaintenanceModule) ||
            !await subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.FinanceModule))
        {
            throw new UnauthorizedAccessException("Чужие пакеты могут содержать заказы и оплаты: требуется доступ к этим разделам.");
        }
    }
}
