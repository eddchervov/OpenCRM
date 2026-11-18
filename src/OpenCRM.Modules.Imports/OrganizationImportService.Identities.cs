using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

public sealed partial class OrganizationImportService
{
    private static readonly Lazy<System.Reflection.PropertyInfo[]> s_semanticProperties = new(() => typeof(ImportCandidate).GetProperties()
        .Where(property => s_editableFields.Contains(property.Name) && property.Name is not ("Excluded" or "ExistingClientId" or "CreateSeparateClient" or "RegistrationCountryCode"))
        .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray());

    private async Task EnsureHistoricalSourceAsync(OrganizationImportDraft draft, CancellationToken cancellationToken)
    {
        if (draft.SourceKey == null && await context.Set<OrganizationImportRow>().AnyAsync(
            row => row.DraftId == draft.Id && row.Kind != "client" && row.Kind != "vehicle" && row.Status != "skip", cancellationToken))
        {
            throw new InvalidDataException("Перед переносом истории укажите постоянное имя базы исходной CRM: оно защищает заказы и оплаты от повторной загрузки.");
        }
    }

    private async Task<List<OrganizationImportIdentity>> ReadIdentitiesAsync(
        OrganizationImportDraft draft, List<ImportCandidate> rows, CancellationToken cancellationToken)
    {
        if (draft.SourceKey == null)
        {
            return [];
        }

        var keys = rows.SelectMany(row => new[] { ExternalKey(row.ExternalId), ExternalKey(row.ClientExternalId), ExternalKey(row.VehicleExternalId) })
            .Where(key => key != null).Distinct().ToArray();

        return await context.Set<OrganizationImportIdentity>().AsNoTracking()
            .Where(identity => identity.OrganizationId == draft.OrganizationId && identity.SourceKey == draft.SourceKey && keys.Contains(identity.ExternalId))
            .ToListAsync(cancellationToken);
    }

    private async Task ApplyIdentityValidationAsync(
        OrganizationImportDraft draft, List<ImportCandidate> rows, List<OrganizationImportIdentity> identities, CancellationToken cancellationToken)
    {
        if (draft.SourceKey == null)
        {
            return;
        }

        var fingerprints = Fingerprints(rows);
        var known = identities.ToDictionary(identity => (identity.Kind, identity.ExternalId));
        var clientIds = identities.Where(identity => identity.Kind == "client").Select(identity => identity.TargetId).ToArray();
        var vehicleIds = identities.Where(identity => identity.Kind == "vehicle").Select(identity => identity.TargetId).ToArray();
        var orderIds = identities.Where(identity => identity.Kind == "order").Select(identity => identity.TargetId).ToArray();
        var available = (await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == draft.OrganizationId && !client.IsDeleted && clientIds.Contains(client.Id))
            .Select(client => client.Id).ToListAsync(cancellationToken)).ToHashSet();
        available.UnionWith(await context.Vehicles.AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == draft.OrganizationId && !vehicle.IsDeleted && vehicleIds.Contains(vehicle.Id))
            .Select(vehicle => vehicle.Id).ToListAsync(cancellationToken));
        available.UnionWith(await context.Maintenance.AsNoTracking()
            .Where(order => order.Vehicle.OrganizationId == draft.OrganizationId && !order.Vehicle.IsDeleted && !order.IsDeleted && orderIds.Contains(order.Id))
            .Select(order => order.Id).ToListAsync(cancellationToken));
        var children = rows.Where(row => row.Kind is "order-line" or "payment" or "history")
            .ToLookup(row => ExternalKey(row.OrderExternalId));
        var duplicates = rows.Where(row => !row.Excluded && row.Kind is "client" or "vehicle" or "order" && ExternalKey(row.ExternalId) != null)
            .GroupBy(row => (row.Kind, Key: ExternalKey(row.ExternalId))).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        foreach (var row in rows.Where(row => !row.Excluded && row.AppliedAt == null && row.Kind is "client" or "vehicle" or "order"))
        {
            var externalId = ExternalKey(row.ExternalId);
            if (duplicates.Contains((row.Kind, externalId)))
            {
                row.Status = "conflict";
                row.Message = "Внешний ID повторяется внутри пакета. Оставьте одну запись для каждого ID.";
                row.MessageCode = ImportRowMessageCodes.IdentityExternalIdDuplicate;
                row.MessageDetails = null;
                row.MessageParameters = null;
                continue;
            }

            if (externalId == null || externalId.StartsWith("@ROW:", StringComparison.Ordinal) || externalId.StartsWith("@PHONE:", StringComparison.Ordinal))
            {
                if (row.Kind == "client" || row.Kind == "vehicle")
                {
                    row.Status = "error";
                    row.Message = "Для защиты между файлами сопоставьте столбец с постоянным внешним ID исходной CRM. Номер строки и телефон не заменяют этот ID.";
                    row.MessageCode = ImportRowMessageCodes.SourceExternalIdRequired;
                    row.MessageDetails = null;
                    row.MessageParameters = null;
                }

                continue;
            }

            if (!known.TryGetValue((row.Kind, externalId), out var identity))
            {
                continue;
            }

            var identical = identity.Fingerprint == fingerprints[row.Id] && available.Contains(identity.TargetId);
            row.Status = identical ? row.Kind == "client" ? "link" : "duplicate" : "conflict";
            row.Message = identical ? "Уже перенесено из этой базы CRM; существующие данные сохранятся." :
                "Этот ID уже переносили, но исходные данные изменились или запись удалена. Автоматическое обновление запрещено; проверьте расхождения.";
            row.MessageCode = identical ? ImportRowMessageCodes.AlreadyImported : ImportRowMessageCodes.IdentityConflict;
            row.MessageDetails = null;
            row.MessageParameters = null;
            row.TargetId = identical ? identity.TargetId : null;
            if (identical && row.Kind == "client")
            {
                row.ResolvedClientId = identity.TargetId;
            }

            if (row.Kind == "order")
            {
                foreach (var child in children[externalId].Where(child => !child.Excluded && child.AppliedAt == null))
                {
                    child.Status = identical ? "duplicate" : "conflict";
                    child.Message = identical ? "Строка входит в ранее перенесённый заказ; повторное создание не требуется." :
                        "Состав ранее перенесённого заказа изменился. Проверьте заказ целиком.";
                    child.MessageCode = identical ? ImportRowMessageCodes.ChildIdentityAlreadyImported : ImportRowMessageCodes.ChildIdentityConflict;
                    child.MessageDetails = null;
                    child.MessageParameters = null;
                }
            }
        }
    }

    private async Task RegisterIdentitiesAsync(
        OrganizationImportDraft draft, List<ImportCandidate> rows, Dictionary<string, OrganizationImportRow> journal, CancellationToken cancellationToken)
    {
        if (draft.SourceKey == null)
        {
            return;
        }

        var fingerprints = Fingerprints(rows);
        var candidates = rows.Where(row => row.Kind is "client" or "vehicle" or "order" &&
            !row.Excluded && ExternalKey(row.ExternalId) != null && journal[row.Id].TargetId != null).ToList();
        var keys = candidates.Select(row => ExternalKey(row.ExternalId)).ToArray();
        var known = await context.Set<OrganizationImportIdentity>().Where(identity => identity.OrganizationId == draft.OrganizationId &&
            identity.SourceKey == draft.SourceKey && keys.Contains(identity.ExternalId)).ToListAsync(cancellationToken);
        foreach (var row in candidates)
        {
            var key = ExternalKey(row.ExternalId)!;
            var target = journal[row.Id].TargetId!.Value;
            var existing = known.SingleOrDefault(identity => identity.Kind == row.Kind && identity.ExternalId == key);
            if (existing != null)
            {
                if (existing.TargetId != target || existing.Fingerprint != fingerprints[row.Id])
                {
                    throw new InvalidDataException("Запись исходной CRM уже была перенесена другим пакетом. Обновите проверку.");
                }

                continue;
            }

            context.Add(new OrganizationImportIdentity
            {
                OrganizationId = draft.OrganizationId, SourceKey = draft.SourceKey, Kind = row.Kind, ExternalId = key,
                ClientId = row.Kind == "client" ? target : null,
                VehicleId = row.Kind == "vehicle" ? target : null,
                OrderId = row.Kind == "order" ? target : null,
                Fingerprint = fingerprints[row.Id]
            });
        }
    }

    private async Task<Dictionary<string, Guid>> PriorTargetsAsync(
        OrganizationImportDraft draft, string kind, List<OrganizationImportIdentity> identities, CancellationToken cancellationToken)
    {
        var candidates = identities.Where(identity => identity.Kind == kind).ToList();
        var ids = candidates.Select(identity => identity.TargetId).ToArray();
        var available = kind == "client"
            ? await context.OrganizationClients.AsNoTracking().Where(client => client.OrganizationId == draft.OrganizationId &&
                !client.IsDeleted && ids.Contains(client.Id)).Select(client => client.Id).ToListAsync(cancellationToken)
            : await context.Vehicles.AsNoTracking().Where(vehicle => vehicle.OrganizationId == draft.OrganizationId &&
                !vehicle.IsDeleted && ids.Contains(vehicle.Id)).Select(vehicle => vehicle.Id).ToListAsync(cancellationToken);
        var availableIds = available.ToHashSet();

        return candidates.Where(identity => availableIds.Contains(identity.TargetId))
            .ToDictionary(identity => identity.ExternalId, identity => identity.TargetId, StringComparer.Ordinal);
    }

    private async Task<List<ImportCandidate>> PrepareIdentityClientsAsync(
        OrganizationImportDraft draft, List<ImportCandidate> rows, List<OrganizationImportIdentity> identities, CancellationToken cancellationToken)
    {
        var targets = await this.PriorTargetsAsync(draft, "client", identities, cancellationToken);
        if (targets.Count == 0)
        {
            return [];
        }

        var fingerprints = Fingerprints(rows.Where(row => row.Kind == "client").ToList());
        var known = identities.Where(identity => identity.Kind == "client").ToDictionary(identity => identity.ExternalId);
        foreach (var row in rows.Where(row => row.Kind == "client" && !row.Excluded && ExternalKey(row.ExternalId) != null))
        {
            var key = ExternalKey(row.ExternalId)!;
            if (known.TryGetValue(key, out var identity) && targets.ContainsKey(key) && identity.Fingerprint == fingerprints[row.Id])
            {
                row.ExistingClientId = identity.TargetId;
            }
        }

        var included = rows.Where(row => row.Kind == "client").Select(row => ExternalKey(row.ExternalId)).ToHashSet();
        var needed = rows.Where(row => row.Kind == "vehicle").Select(row => ExternalKey(row.ClientExternalId))
            .Where(key => key != null && !included.Contains(key) && targets.ContainsKey(key)).Distinct().ToList();
        var ids = needed.Select(key => targets[key!]).ToArray();
        var clients = await context.OrganizationClients.AsNoTracking().Where(client => client.OrganizationId == draft.OrganizationId && ids.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, cancellationToken);

        return needed.Select(key => new ImportCandidate
        {
            Id = $"@previous:{key}", Kind = "client", ExternalId = key, Name = clients[targets[key!]].Name,
            ExistingClientId = targets[key!]
        }).ToList();
    }

    private static Dictionary<string, string> Fingerprints(List<ImportCandidate> rows)
    {
        var children = rows.Where(row => row.Kind is "order-line" or "payment" or "history" && !row.Excluded)
            .ToLookup(row => ExternalKey(row.OrderExternalId));
        var result = new Dictionary<string, string>();
        foreach (var row in rows.Where(row => row.Kind is "client" or "vehicle" or "order"))
        {
            var values = new List<string> { SemanticValue(row) };
            if (row.Kind == "order")
            {
                values.AddRange(children[ExternalKey(row.ExternalId)].OrderBy(child => child.Kind, StringComparer.Ordinal)
                    .ThenBy(child => child.TableId, StringComparer.Ordinal).ThenBy(child => child.SourceRow).Select(SemanticValue));
            }

            result[row.Id] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
        }

        return result;
    }

    private static string SemanticValue(ImportCandidate row)
    {
        var values = s_semanticProperties.Value
            .ToDictionary(property => property.Name, property => property.GetValue(row));
        if (row.Kind == "vehicle")
        {
            try
            {
                var countryCode = InternationalRegistrationPlateRules.NormalizeCountryCode(row.RegistrationCountryCode);
                if (countryCode is not (null or "RU"))
                {
                    values["RegistrationCountryCode"] = countryCode;
                }
            }
            catch (InvalidOperationException)
            {
                // Ошибочный код страны уже блокирует импорт и не должен останавливать расчёт остальных строк.
            }
        }

        values["Kind"] = row.Kind;

        return JsonSerializer.Serialize(values);
    }
}
