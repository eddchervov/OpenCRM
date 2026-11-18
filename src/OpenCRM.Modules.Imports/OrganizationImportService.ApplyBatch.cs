using System.Data;
using System.Text.Json;
using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Imports;

public sealed partial class OrganizationImportService
{
    private async Task EnsureBusinessAccessAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var kinds = await context.Set<OrganizationImportRow>().Where(row => row.DraftId == id)
            .Select(row => row.Kind).Distinct().ToListAsync(cancellationToken);
        if (kinds.Any(kind => kind is "order" or "order-line" or "payment" or "history"))
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
            if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.MaintenanceModule))
            {
                throw new UnauthorizedAccessException("Для переноса заказов требуется доступ к модулю заказов.");
            }
        }
        if (kinds.Contains("payment"))
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.FinanceView);
            if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.FinanceModule))
            {
                throw new UnauthorizedAccessException("Для переноса оплат требуется доступ к финансам.");
            }
        }
    }

    private async Task ValidateBatchRowsAsync(OrganizationImportDraft draft, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        var identities = await this.ReadIdentitiesAsync(draft, rows, cancellationToken);
        var dependencies = await this.PrepareIdentityClientsAsync(draft, rows, identities, cancellationToken);
        var applied = await context.Set<OrganizationImportRow>().AsNoTracking()
            .Where(row => row.DraftId == draft.Id && row.AppliedAt != null).ToDictionaryAsync(row => row.Id, cancellationToken);
        var originalApplied = rows.Where(row => applied.ContainsKey(row.Id)).ToDictionary(row => row.Id, row => JsonSerializer.Serialize(row));
        var selectedClients = rows.Where(row => row.Kind == "client").ToDictionary(row => row.Id, row => row.ExistingClientId);
        foreach (var row in rows.Where(row => row.Kind == "client" && applied.ContainsKey(row.Id)))
        {
            row.ExistingClientId = applied[row.Id].TargetId;
        }

        await this.ValidateAsync(draft.RequestedBy ?? draft.UserId, draft.OrganizationId, rows.Concat(dependencies).ToList(), cancellationToken);
        var vehicleIds = await this.PriorTargetsAsync(draft, "vehicle", identities, cancellationToken);
        var seenVehicles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.Where(row => row.Kind == "vehicle" && !row.Excluded && !string.IsNullOrWhiteSpace(row.ExternalId)))
        {
            if (!seenVehicles.Add(ExternalKey(row.ExternalId)!))
            {
                row.Status = "error";
                row.Message = "Повторяется внешний ID автомобиля. Исправьте ID перед переносом заказов.";
                row.MessageCode = ImportRowMessageCodes.DuplicateVehicleExternalId;
                row.MessageDetails = null;
                row.MessageParameters = null;
            }
            vehicleIds[ExternalKey(row.ExternalId)!] = applied.GetValueOrDefault(row.Id)?.TargetId ??
                vehicleIds.GetValueOrDefault(ExternalKey(row.ExternalId)!, Guid.NewGuid());
        }

        var business = rows.Where(IsBusinessRow).Select(ToBusinessRecord).ToList();
        if (business.Count > 0)
        {
            if (businessRecords == null)
            {
                throw new InvalidOperationException("Не зарегистрирована обработка исторических заказов.");
            }

            var orderIds = rows.Where(row => row.Kind == "order" && !row.Excluded && !string.IsNullOrWhiteSpace(row.ExternalId))
                .GroupBy(row => row.ExternalId!, StringComparer.Ordinal)
                .Where(group => group.Count() == 1).ToDictionary(group => group.Key,
                    group => applied.GetValueOrDefault(group.Single().Id)?.TargetId ?? Guid.NewGuid(), StringComparer.Ordinal);
            var clientIds = rows.Where(row => row.Kind == "client" && row.Status is "create" or "link" && !string.IsNullOrWhiteSpace(row.ExternalId))
                .GroupBy(row => ExternalKey(row.ExternalId)!, StringComparer.Ordinal).Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => applied.GetValueOrDefault(group.Single().Id)?.TargetId ??
                    group.Single().ResolvedClientId ?? Guid.NewGuid(), StringComparer.Ordinal);
            foreach (var prior in await this.PriorTargetsAsync(draft, "client", identities, cancellationToken))
            {
                clientIds.TryAdd(prior.Key, prior.Value);
            }
            await businessRecords.ValidateAsync(draft.RequestedBy ?? draft.UserId, draft.OrganizationId, business, vehicleIds, clientIds, orderIds, cancellationToken);
            var results = business.ToDictionary(row => row.Id);
            foreach (var row in rows.Where(IsBusinessRow))
            {
                row.Status = results[row.Id].Status;
                row.Message = results[row.Id].Message;
                row.MessageCode = results[row.Id].MessageCode;
                row.MessageParameters = results[row.Id].MessageParameters;
                row.MessageDetails = results[row.Id].MessageDetails;
            }

            var invalidVehicles = rows.Where(row => row.Kind == "vehicle" && (row.Status is "error" or "conflict" || row.Excluded))
                .Select(row => row.ExternalId).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows.Where(row => row.Kind == "order" && !row.Excluded && invalidVehicles.Contains(row.VehicleExternalId)))
            {
                row.Status = "error";
                row.Message = "Автомобиль заказа исключён или не прошёл проверку.";
                row.MessageCode = ImportRowMessageCodes.VehicleNotReady;
                row.MessageDetails = null;
                row.MessageParameters = null;
            }
        }

        await this.ApplyIdentityValidationAsync(draft, rows, identities, cancellationToken);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (originalApplied.TryGetValue(row.Id, out var saved))
            {
                rows[index] = DeserializeCandidate(saved);
            }
            else if (row.Kind == "client")
            {
                row.ExistingClientId = selectedClients[row.Id];
            }

            if (new[] { row.ExternalId, row.OrderExternalId, row.VehicleExternalId }.Any(value => value?.Length > 100))
            {
                row.Status = "error";
                row.Message = "Внешние идентификаторы ограничены 100 символами.";
                row.MessageCode = ImportRowMessageCodes.ExternalIdentityTooLong;
                row.MessageDetails = null;
                row.MessageParameters = null;
            }
        }
    }

    private async Task PersistValidationAsync(Guid draftId, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        var validated = rows.ToDictionary(row => row.Id);
        var position = 0;
        while (true)
        {
            var entities = await context.Set<OrganizationImportRow>().Where(row => row.DraftId == draftId)
                .OrderBy(row => row.Position).Skip(position).Take(500).ToListAsync(cancellationToken);
            if (entities.Count == 0)
            {
                break;
            }

            foreach (var entity in entities)
            {
                if (!entity.AppliedAt.HasValue)
                {
                    UpdateRow(entity, validated[entity.Id]);
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            foreach (var entity in entities)
            {
                context.Entry(entity).State = EntityState.Detached;
            }

            position += entities.Count;
        }
    }

    private async Task ApplyBatchAsync(OrganizationImportDraft initialDraft, CancellationToken cancellationToken)
    {
        var draftId = initialDraft.Id;
        var organizationId = initialDraft.OrganizationId;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
            var draft = await context.Set<OrganizationImportDraft>().SingleAsync(row => row.Id == draftId, cancellationToken);
            if (draft.State is not ("import-queued" or "importing"))
            {
                return;
            }

            var userId = draft.RequestedBy ?? draft.UserId;
            await this.EnsureAccessAsync(userId, organizationId);
            await this.EnsureBusinessAccessAsync(userId, organizationId, draftId, cancellationToken);
            await this.EnsureHistoricalSourceAsync(draft, cancellationToken);
            if (draft.State == "import-queued")
            {
                var all = await this.ReadCandidatesAsync(draftId, cancellationToken);
                var reviewed = JsonSerializer.Serialize(all);
                await this.ValidateBatchRowsAsync(draft, all, cancellationToken);
                if (all.Any(row => row.Status is "error" or "conflict") || reviewed != JsonSerializer.Serialize(all))
                {
                    await this.PersistValidationAsync(draftId, all, cancellationToken);
                    var metadata = ReadData(draft);
                    metadata.Rows = [];
                    metadata.Reconciliation = ImportReconciliationBuilder.Build(all);
                    draft.PayloadJson = JsonSerializer.Serialize(metadata);
                    draft.State = draft.ProcessedRows > 0 ? "paused" : "ready";
                    draft.Error = "Данные организации изменились. Проверьте обновлённый результат перед продолжением.";
                    draft.Revision++;
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            var journal = await this.SelectApplyBatchAsync(draftId, cancellationToken);
            var entries = journal.ToDictionary(row => row.Id);
            var batch = journal.Select(row => DeserializeCandidate(row.PayloadJson)).ToList();
            await this.EnsureBatchQuotaAsync(draft, batch, cancellationToken);

            if (!await this.RecheckNextBatchAsync(draft, batch, cancellationToken))
            {
                foreach (var row in batch)
                {
                    UpdateRow(entries[row.Id], row);
                }

                var candidates = await this.ReadCandidatesAsync(draftId, cancellationToken);
                var changed = batch.ToDictionary(row => row.Id);
                var metadata = ReadData(draft);
                metadata.Rows = [];
                metadata.Reconciliation = ImportReconciliationBuilder.Build(candidates.Select(row => changed.GetValueOrDefault(row.Id, row)).ToList());
                draft.PayloadJson = JsonSerializer.Serialize(metadata);

                draft.State = draft.ProcessedRows > 0 ? "paused" : "ready";
                draft.Error = "Данные организации изменились во время переноса. Проверьте обновлённые строки перед продолжением.";
                draft.Revision++;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            var result = draft.ResultJson == null ? new ImportResultDto() : JsonSerializer.Deserialize<ImportResultDto>(draft.ResultJson)!;
            var sourceClientIds = batch.Select(row => row.ResolvedClientRowId).Where(id => id != null).ToArray();
            var clientIds = await context.Set<OrganizationImportRow>().Where(row => row.DraftId == draftId &&
                row.Kind == "client" && row.TargetId != null && sourceClientIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.TargetId!.Value, cancellationToken);
            var vehicleIds = await this.GetExternalTargetsAsync(draftId, "vehicle", batch.Select(row => row.VehicleExternalId).ToArray(), cancellationToken);
            var orderIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var row in batch.Where(row => row.Kind == "client" && row.Status is "create" or "link"))
            {
                var entry = entries[row.Id];
                if (row.Status == "link")
                {
                    entry.TargetId = row.ResolvedClientId;
                    result.LinkedClients++;
                    continue;
                }

                var applied = this.ApplyRows(userId, organizationId, draftId, [row]);
                entry.TargetId = applied.ClientIds.Single();
                clientIds[row.Id] = entry.TargetId.Value;
                result.CreatedClients++;
                if (result.ClientIds.Count < 100)
                {
                    result.ClientIds.Add(entry.TargetId.Value);
                }
            }

            foreach (var row in batch.Where(row => row.Kind == "vehicle" && row.Status == "create"))
            {
                if (row.ResolvedClientRowId != null && clientIds.TryGetValue(row.ResolvedClientRowId, out var clientId))
                {
                    row.ResolvedClientId = clientId;
                }

                var applied = this.ApplyRows(userId, organizationId, draftId, [row]);
                entries[row.Id].TargetId = applied.VehicleIds.Single();
                result.CreatedVehicles++;
                if (result.VehicleIds.Count < 100)
                {
                    result.VehicleIds.Add(applied.VehicleIds.Single());
                }
            }

            var duplicateVehicles = batch.Where(row => row.Kind == "vehicle" && row.Status == "duplicate").ToList();
            var existingVehicles = duplicateVehicles.Count == 0 ? [] : await context.Vehicles.AsNoTracking()
                .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
                .Select(vehicle => new { vehicle.Id, vehicle.Vin, vehicle.RegistrationPlate, vehicle.RegistrationCountryCode }).ToListAsync(cancellationToken);
            var vehiclesByVin = existingVehicles.Where(vehicle => NormalizeVehicleKey(vehicle.Vin).Length > 0)
                .ToLookup(vehicle => NormalizeVehicleKey(vehicle.Vin));
            var vehiclesByPlate = existingVehicles.Where(vehicle => NormalizePlateMatchKey(vehicle.RegistrationPlate, vehicle.RegistrationCountryCode).Length > 0)
                .ToLookup(vehicle => NormalizePlateMatchKey(vehicle.RegistrationPlate, vehicle.RegistrationCountryCode), StringComparer.Ordinal);
            foreach (var row in duplicateVehicles)
            {
                if (row.TargetId.HasValue)
                {
                    entries[row.Id].TargetId = row.TargetId;
                    continue;
                }

                var candidates = vehiclesByVin[NormalizeVehicleKey(row.Vin)].Concat(vehiclesByPlate[NormalizePlateMatchKey(row.Plate, row.RegistrationCountryCode)])
                    .Select(vehicle => vehicle.Id).Distinct().ToList();
                if (candidates.Count != 1)
                {
                    throw new InvalidDataException("Существующий автомобиль изменился. Проверьте связи перед продолжением.");
                }

                entries[row.Id].TargetId = candidates.Single();
            }

            var business = batch.Where(row => IsBusinessRow(row) && row.Status == "create").Select(ToBusinessRecord).ToList();
            foreach (var row in batch.Where(row => row.Kind == "order" && row.Status == "duplicate"))
            {
                entries[row.Id].TargetId = row.TargetId;
            }

            if (business.Count > 0)
            {
                var partyIds = await this.GetExternalTargetsAsync(draftId, "client", batch.Select(row => row.ClientExternalId).ToArray(), cancellationToken);
                var applied = await businessRecords!.ApplyAsync(userId, organizationId, business, vehicleIds, partyIds, orderIds, cancellationToken);
                foreach (var row in business)
                {
                    entries[row.Id].TargetId = row.TargetId;
                }
                result.CreatedOrders += applied.CreatedOrders;
                result.CreatedOrderLines += applied.CreatedOrderLines;
                result.CreatedPayments += applied.CreatedPayments;
                result.CreatedHistoryEvents += applied.CreatedHistoryEvents;
                result.OrderIds.AddRange(applied.OrderIds.Take(Math.Max(0, 100 - result.OrderIds.Count)));
                result.PaymentIds.AddRange(applied.PaymentIds.Take(Math.Max(0, 100 - result.PaymentIds.Count)));
            }

            foreach (var row in batch)
            {
                var entry = entries[row.Id];
                entry.AppliedAt = DateTime.UtcNow;
                row.AppliedAt = entry.AppliedAt;
                row.TargetId = entry.TargetId;
                if (row.Status == "create")
                {
                    row.Message = "Создано.";
                    row.MessageCode = ImportRowMessageCodes.StatusCreated;
                    row.MessageDetails = null;
                    row.MessageParameters = null;
                }

                UpdateRow(entry, row);

                if (row.Status is "skip" or "duplicate")
                {
                    result.Skipped++;
                }
            }

            draft.ProcessedRows += batch.Count;
            draft.State = draft.ProcessedRows == draft.TotalRows ? "completed" : "importing";
            if (draft.State == "completed")
            {
                draft.ImportedAt = DateTime.UtcNow;
            }

            draft.ResultJson = JsonSerializer.Serialize(result);
            draft.Revision++;
            draft.UpdatedAt = DateTime.UtcNow;
            draft.Error = null;
            await this.RegisterIdentitiesAsync(draft, batch, entries, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task EnsureBatchQuotaAsync(
        OrganizationImportDraft draft,
        List<ImportCandidate> batch,
        CancellationToken cancellationToken)
    {
        var userId = draft.RequestedBy ?? draft.UserId;
        var newClients = batch.Count(row => row.Kind == "client" && row.Status == "create");
        if (newClients > 0)
        {
            var clientLimit = Helpers.PlatformDefaults.NormalizeLimit((await subscriptions.GetFeatureSnapshotAsync(userId, draft.OrganizationId))
                .Features.FirstOrDefault(feature => feature.Code == FeatureCodes.WorkspaceClientsLimit)?.IntValue ?? int.MaxValue);
            var clientCount = await context.OrganizationClients.CountAsync(
                client => client.OrganizationId == draft.OrganizationId,
                cancellationToken);
            if ((long)clientCount + newClients > clientLimit)
            {
                throw new InvalidDataException(
                    $"Лимит клиентов изменился перед следующей партией: сейчас {clientCount}, в партии {newClients}, лимит {clientLimit}. Уже применённые партии сохранены; освободите места или скорректируйте пакет перед возобновлением.");
            }
        }

        var newVehicles = batch.Count(row => row.Kind == "vehicle" && row.Status == "create");
        if (newVehicles == 0)
        {
            return;
        }

        var vehicleLimit = await vehicleLimits.GetVehicleLimitInfoAsync(userId, draft.OrganizationId);
        if (vehicleLimit.MaxVehiclesForUser > 0 &&
            (long)vehicleLimit.CurrentVehicleCount + newVehicles > vehicleLimit.MaxVehiclesForUser)
        {
            throw new InvalidDataException(
                $"Лимит техники изменился перед следующей партией: сейчас {vehicleLimit.CurrentVehicleCount}, в партии {newVehicles}, лимит {vehicleLimit.MaxVehiclesForUser}. Уже применённые партии сохранены; освободите места или скорректируйте пакет перед возобновлением.");
        }
    }

    private async Task<Dictionary<string, Guid>> GetExternalTargetsAsync(
        Guid draftId, string kind, string?[] externalIds, CancellationToken cancellationToken)
    {
        externalIds = externalIds.Select(ExternalKey).ToArray();
        var matches = await context.Set<OrganizationImportRow>().AsNoTracking().Where(row => row.DraftId == draftId &&
            row.Kind == kind && row.ExternalId != null && externalIds.Contains(row.ExternalId) && row.TargetId != null)
            .Select(row => new { row.ExternalId, row.TargetId }).ToListAsync(cancellationToken);

        var result = matches.ToDictionary(row => row.ExternalId!, row => row.TargetId!.Value, StringComparer.Ordinal);
        var draft = await context.Set<OrganizationImportDraft>().AsNoTracking().SingleAsync(row => row.Id == draftId, cancellationToken);
        if (draft.SourceKey != null)
        {
            var identities = await context.Set<OrganizationImportIdentity>().AsNoTracking().Where(identity =>
                identity.OrganizationId == draft.OrganizationId && identity.SourceKey == draft.SourceKey &&
                identity.Kind == kind && externalIds.Contains(identity.ExternalId)).ToListAsync(cancellationToken);
            foreach (var identity in identities)
            {
                result.TryAdd(identity.ExternalId, identity.TargetId);
            }
        }

        return result;
    }

    private async Task<bool> RecheckNextBatchAsync(OrganizationImportDraft draft, List<ImportCandidate> batch, CancellationToken cancellationToken)
    {
        if (batch.All(IsBusinessRow))
        {
            var expectedStatus = batch.ToDictionary(row => row.Id, row => row.Status);
            var identities = await this.ReadIdentitiesAsync(draft, batch, cancellationToken);
            await this.ApplyIdentityValidationAsync(draft, batch, identities, cancellationToken);

            return batch.All(row => row.Status == expectedStatus[row.Id]);
        }

        var sourceIds = batch.Select(row => ExternalKey(row.ClientExternalId)).Where(id => id != null).ToArray();
        var sourcePhones = batch.Select(row => row.ClientPhone).Where(phone => !string.IsNullOrWhiteSpace(phone)).ToArray();
        var dependenciesQuery = context.Set<OrganizationImportRow>().AsNoTracking()
            .Where(row => row.DraftId == draft.Id && row.Kind == "client");
        if (sourcePhones.Length == 0)
        {
            dependenciesQuery = dependenciesQuery.Where(row => sourceIds.Contains(row.ExternalId));
        }

        var dependencies = await dependenciesQuery.ToListAsync(cancellationToken);
        var clients = dependencies.Select(entity =>
        {
            var row = DeserializeCandidate(entity.PayloadJson);
            if (entity.AppliedAt.HasValue)
            {
                row.ExistingClientId = entity.TargetId;
            }

            return row;
        }).Where(row => batch.All(item => item.Id != row.Id)).ToList();
        var appliedClientIds = dependencies.Where(row => row.TargetId.HasValue).ToDictionary(row => row.Id, row => row.TargetId!.Value);
        var expected = batch.ToDictionary(row => row.Id, row => new
        {
            row.Status,
            ClientId = row.ResolvedClientId ?? (row.ResolvedClientRowId != null ? appliedClientIds.GetValueOrDefault(row.ResolvedClientRowId) : (Guid?)null)
        });
        var validationRows = clients.Concat(batch).ToList();
        var identityRows = await this.ReadIdentitiesAsync(draft, validationRows, cancellationToken);
        validationRows.AddRange(await this.PrepareIdentityClientsAsync(draft, validationRows, identityRows, cancellationToken));
        await this.ValidateAsync(draft.RequestedBy ?? draft.UserId, draft.OrganizationId, validationRows, cancellationToken);
        await this.ApplyIdentityValidationAsync(draft, batch, identityRows, cancellationToken);

        return batch.All(row => expected[row.Id].Status == row.Status &&
            (row.Kind != "vehicle" || expected[row.Id].ClientId == (row.ResolvedClientId ??
                (row.ResolvedClientRowId != null ? appliedClientIds.GetValueOrDefault(row.ResolvedClientRowId) : (Guid?)null))));
    }

    private async Task<List<OrganizationImportRow>> SelectApplyBatchAsync(Guid draftId, CancellationToken cancellationToken)
    {
        var pending = context.Set<OrganizationImportRow>().Where(row => row.DraftId == draftId && row.AppliedAt == null);
        foreach (var kind in new[] { "client", "vehicle" })
        {
            var batch = await pending.Where(row => row.Kind == kind).OrderBy(row => row.Position).Take(250).ToListAsync(cancellationToken);
            if (batch.Count > 0)
            {
                return batch;
            }
        }

        var skipped = await pending.Where(row => (row.Status == "skip" || row.Status == "duplicate") &&
            row.Kind != "order" && row.ParentExternalId == null)
            .OrderBy(row => row.Position).Take(250).ToListAsync(cancellationToken);
        if (skipped.Count > 0)
        {
            return skipped;
        }

        var orders = await pending.Where(row => row.Kind == "order").OrderBy(row => row.Position).Take(25).ToListAsync(cancellationToken);
        var orderIds = orders.Select(row => row.ExternalId).ToArray();
        var ids = orders.Select(row => row.Id).ToArray();
        var aggregate = await pending.Where(row => ids.Contains(row.Id) || row.ParentExternalId != null && orderIds.Contains(row.ParentExternalId))
            .OrderBy(row => row.Position).Take(25026).ToListAsync(cancellationToken);
        if (aggregate.Count == 0 && await pending.AnyAsync(cancellationToken))
        {
            throw new InvalidDataException("Остались строки без доступного родительского заказа. Проверьте связи.");
        }

        if (aggregate.Count > 25_025)
        {
            throw new InvalidDataException("Пакет заказов превышает разрешённый размер агрегатов.");
        }

        var selected = new List<OrganizationImportRow>();
        foreach (var order in orders)
        {
            var children = aggregate.Where(row => row.Id == order.Id || row.ParentExternalId == order.ExternalId).ToList();
            if (children.Count > 1001)
            {
                throw new InvalidDataException("В одном заказе допускается до 1000 дочерних строк.");
            }

            if (selected.Count > 0 && selected.Count + children.Count > 500)
            {
                break;
            }

            selected.AddRange(children);
        }

        return selected;
    }

    private static bool IsBusinessRow(ImportCandidate row) => row.Kind is "order" or "order-line" or "payment" or "history";

    private static string NormalizeVehicleKey(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string NormalizePlateMatchKey(string? plate, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return string.Empty;
        }

        return countryCode is null or "RU"
            ? NormalizeVehicleKey(plate)
            : $"{countryCode}\0{plate.Trim()}";
    }

    private static ImportBusinessRecord ToBusinessRecord(ImportCandidate row)
    {
        var record = JsonSerializer.Deserialize<ImportBusinessRecord>(JsonSerializer.Serialize(row))!;
        if (row.Kind == "order-line")
        {
            record.DetailName = row.Name;
        }

        return record;
    }
}
