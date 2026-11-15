using Microsoft.EntityFrameworkCore;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Сервис команд по клиентам организации. </summary>
internal sealed class OrganizationClientCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService,
    ClientsAccess clientsAccess,
    OrganizationClientTransaction transaction,
    OrganizationClientTagWriter tagWriter,
    OrganizationClientHistoryWriter historyWriter,
    OrganizationClientResponsibleResolver responsibleResolver,
    IQuickBookingClientLookupService quickBookingClientLookup) : IOrganizationClientCommandService
{
    /// <summary> Находит или создаёт клиента после блокировки организации, исключая конкурентные дубли. </summary>
    public async Task<(Guid? ClientId, bool Created)> ResolveQuickBookingClientAsync(
        string userId,
        Guid organizationId,
        string? phone,
        string? name,
        string createdComment)
    {
        phone = ClientRules.NormalizeNullable(phone);
        name = ClientRules.NormalizeNullable(name);
        if (phone == null && name == null)
        {
            return (null, false);
        }

        if (phone != null && ClientPhoneSearch.GetDigits(phone).Length is < 7 or > 15)
        {
            throw new InvalidOperationException("Укажите корректный номер телефона: от 7 до 15 цифр.");
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);
        Guid? resolvedClientId = null;

        return await transaction.ExecuteAsync(async () =>
        {
            await transaction.LockOrganizationAsync(organizationId);
            resolvedClientId = await quickBookingClientLookup.FindExistingAsync(userId, organizationId, phone, name);
            if (resolvedClientId.HasValue)
            {
                return (resolvedClientId, false);
            }

            resolvedClientId = await this.CreateAsync(userId, organizationId, new CreateOrganizationClientDto
            {
                Name = name ?? $"Клиент {phone}",
                Phone = phone,
                PartyTypeCode = "person",
                Comment = createdComment
            });

            return (resolvedClientId, true);
        }, () => context.OrganizationClients.AsNoTracking().AnyAsync(client =>
            client.Id == resolvedClientId && client.OrganizationId == organizationId && !client.IsDeleted));
    }

    /// <summary> Создаёт клиента организации. </summary>
    public Task<Guid> CreateAsync(string userId, Guid organizationId, CreateOrganizationClientDto dto) =>
        this.CreateAsync(userId, organizationId, dto, rejectPhoneDuplicate: false);

    /// <summary> Создаёт клиента при конвертации обращения, отклоняя совпадение телефона. </summary>
    public Task<Guid> CreateForInquiryBookingAsync(string userId, Guid organizationId, CreateOrganizationClientDto dto) =>
        this.CreateAsync(userId, organizationId, dto, rejectPhoneDuplicate: true);

    private async Task<Guid> CreateAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationClientDto dto,
        bool rejectPhoneDuplicate)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsCreate);

        var now = DateTime.UtcNow;
        var statusCode = OrganizationClientStatusCodes.Normalize(dto.StatusCode);
        var responsibleUserId = await responsibleResolver.ResolveAsync(organizationId, dto.ResponsibleUserId);
        var partyTypeCode = ClientRules.NormalizePartyTypeCode(dto.PartyTypeCode);
        var identity = ClientRules.NormalizeIdentity(dto.Name, partyTypeCode, dto.LastName, dto.FirstName, dto.MiddleName);
        var companyTypeCode = ClientRules.NormalizeCompanyType(dto.CompanyTypeCode, partyTypeCode);
        var phone = ClientRules.NormalizeAccountingField(dto.Phone, 40, "Телефон");
        var clientId = Guid.NewGuid();

        return await transaction.ExecuteAsync(async () =>
        {
            var client = new OrganizationClient
            {
                Id = clientId,
                OrganizationId = organizationId,
                Name = identity.Name,
                LastName = identity.LastName,
                FirstName = identity.FirstName,
                MiddleName = identity.MiddleName,
                CompanyTypeCode = companyTypeCode,
                Phone = phone,
                PartyTypeCode = partyTypeCode,
                Inn = partyTypeCode == "company" ? ClientRules.NormalizeInn(dto.Inn, companyTypeCode) : null,
                Kpp = partyTypeCode == "company" ? ClientRules.NormalizeKpp(dto.Kpp, companyTypeCode) : null,
                LegalAddress = partyTypeCode == "company" ? ClientRules.NormalizeAccountingField(dto.LegalAddress, 500, "Юридический адрес") : null,
                Comment = ClientRules.NormalizeComment(dto.Comment, dto.Notes),
                StatusCode = statusCode,
                ResponsibleUserId = responsibleUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            await transaction.LockOrganizationAsync(organizationId);
            await this.EnsureClientLimitAsync(userId, organizationId);

            if (rejectPhoneDuplicate
                && await quickBookingClientLookup.FindPotentialDuplicateAsync(userId, organizationId, phone, null) is not null)
            {
                throw new InvalidOperationException("Найден клиент с таким телефоном. Выберите существующего клиента.");
            }

            context.OrganizationClients.Add(client);
            await tagWriter.SyncAsync(organizationId, client, dto.TagNames);
            historyWriter.AddHistory(client.Id, organizationId, userId, "created", "Клиент создан", null, null, null, now);
            await historyWriter.AddAuditAsync(organizationId, userId, "clients.created", "Клиент создан", client.Name, client.Id);
            await context.SaveChangesAsync();

            return client.Id;
        }, async () => await context.OrganizationClients.AsNoTracking()
            .AnyAsync(existingClient => existingClient.Id == clientId && existingClient.OrganizationId == organizationId));
    }

    private async Task EnsureClientLimitAsync(string userId, Guid organizationId)
    {
        var feature = (await organizationSubscriptionService.GetFeatureSnapshotAsync(userId, organizationId))
            .Features.FirstOrDefault(item => item.Code == FeatureCodes.WorkspaceClientsLimit);
        var limit = Helpers.PlatformDefaults.NormalizeLimit(feature?.IntValue ?? int.MaxValue);
        var count = await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId);

        if (count >= limit)
        {
            throw new InvalidOperationException($"Достигнут лимит клиентов организации: {count} из {limit}.");
        }
    }

    /// <summary> Обновляет данные клиента организации. </summary>
    public async Task UpdateAsync(string userId, Guid organizationId, Guid clientId, UpdateOrganizationClientDto dto)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);

        var now = DateTime.UtcNow;
        var responsibleUserId = await responsibleResolver.ResolveAsync(organizationId, dto.ResponsibleUserId);
        var partyTypeCode = ClientRules.NormalizePartyTypeCode(dto.PartyTypeCode);
        var identity = ClientRules.NormalizeIdentity(dto.Name, partyTypeCode, dto.LastName, dto.FirstName, dto.MiddleName);
        var companyTypeCode = ClientRules.NormalizeCompanyType(dto.CompanyTypeCode, partyTypeCode);
        var phone = ClientRules.NormalizeAccountingField(dto.Phone, 40, "Телефон");
        var legalAddress = partyTypeCode == "company" ? ClientRules.NormalizeAccountingField(dto.LegalAddress, 500, "Юридический адрес") : null;
        var comment = ClientRules.NormalizeComment(dto.Comment, dto.Notes);
        var statusCode = OrganizationClientStatusCodes.Normalize(dto.StatusCode);
        Guid? savedHistoryId = null;

        await transaction.ExecuteAsync(async () =>
        {
            savedHistoryId = null;
            await transaction.LockOrganizationAsync(organizationId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationClients" WHERE "Id" = {clientId} AND "OrganizationId" = {organizationId} FOR UPDATE""");

            var client = await context.OrganizationClients
                .FirstOrDefaultAsync(existingClient => existingClient.Id == clientId
                    && existingClient.OrganizationId == organizationId
                    && !existingClient.IsDeleted)
                ?? throw new InvalidOperationException("Клиент не найден.");

            await context.Entry(client).ReloadAsync();

            var trackedAssignments = context.ChangeTracker.Entries<OrganizationClientTagAssignment>()
                .Where(entry => entry.Entity.OrganizationClientId == clientId)
                .ToList();

            foreach (var assignmentEntry in trackedAssignments)
            {
                assignmentEntry.State = EntityState.Detached;
            }

            client.TagAssignments.Clear();
            await context.Entry(client).Collection(existingClient => existingClient.TagAssignments)
                .Query()
                .Include(assignment => assignment.OrganizationClientTag)
                .LoadAsync();

            var previousName = client.Name;
            var previousPhone = client.Phone;
            var previousPartyTypeCode = client.PartyTypeCode;
            var previousCompanyTypeCode = client.CompanyTypeCode;
            var previousInn = client.Inn;
            var previousKpp = client.Kpp;
            var previousLegalAddress = client.LegalAddress;
            var previousComment = client.Comment;
            var previousStatusCode = OrganizationClientStatusCodes.Normalize(client.StatusCode);
            var previousResponsibleUserId = client.ResponsibleUserId;
            var previousTags = client.TagAssignments
                .Select(assignment => assignment.OrganizationClientTag.Name)
                .OrderBy(tagName => tagName)
                .ToList();

            // Прежний клиент API не передаёт составные поля: сохраняем их при неизменном имени.
            var preserveNameParts = partyTypeCode == "person" && client.PartyTypeCode == "person"
                && identity.FirstName == null && string.Equals(client.Name, identity.Name, StringComparison.Ordinal);
            client.LastName = preserveNameParts ? client.LastName : identity.LastName;
            client.FirstName = preserveNameParts ? client.FirstName : identity.FirstName;
            client.MiddleName = preserveNameParts ? client.MiddleName : identity.MiddleName;
            client.CompanyTypeCode = companyTypeCode ?? (partyTypeCode == "company" ? client.CompanyTypeCode : null);
            client.Name = identity.Name;
            client.Phone = phone;
            client.PartyTypeCode = partyTypeCode;
            client.Inn = partyTypeCode == "company" ? ClientRules.NormalizeInn(dto.Inn, client.CompanyTypeCode) : null;
            client.Kpp = partyTypeCode == "company" ? ClientRules.NormalizeKpp(dto.Kpp, client.CompanyTypeCode) : null;
            client.LegalAddress = legalAddress;
            client.Comment = comment;
            client.StatusCode = statusCode;
            client.ResponsibleUserId = responsibleUserId;
            client.UpdatedAt = now;
            await tagWriter.SyncAsync(organizationId, client, dto.TagNames);
            var currentTags = client.TagAssignments
                .Select(assignment => assignment.OrganizationClientTag.Name)
                .OrderBy(tagName => tagName)
                .ToList();

            var changes = new List<string>();
            ClientRules.AddChange(changes, "Имя или название", previousName, client.Name);
            ClientRules.AddChange(changes, "Телефон", previousPhone, client.Phone);
            ClientRules.AddChange(changes, "Тип клиента", previousPartyTypeCode, client.PartyTypeCode);
            ClientRules.AddChange(changes, "Вид организации", previousCompanyTypeCode, client.CompanyTypeCode);
            ClientRules.AddChange(changes, "ИНН", previousInn, client.Inn);
            ClientRules.AddChange(changes, "КПП", previousKpp, client.Kpp);
            ClientRules.AddChange(changes, "Юридический адрес", previousLegalAddress, client.LegalAddress);
            if (!string.Equals(previousComment, client.Comment, StringComparison.Ordinal))
            {
                changes.Add(client.Comment == null ? "Комментарий удалён." : "Комментарий изменён.");
            }
            ClientRules.AddChange(changes, "Статус", OrganizationClientStatusCodes.GetName(previousStatusCode), OrganizationClientStatusCodes.GetName(client.StatusCode));
            ClientRules.AddChange(changes, "Ответственный", previousResponsibleUserId, client.ResponsibleUserId);
            ClientRules.AddChange(changes, "Теги", string.Join(", ", previousTags), string.Join(", ", currentTags));
            if (changes.Count > 0)
            {
                savedHistoryId = historyWriter.AddHistory(client.Id, organizationId, userId, "updated", "Клиент изменен", string.Join(Environment.NewLine, changes), null, null, now);
                await historyWriter.AddAuditAsync(organizationId, userId, "clients.updated", "Клиент изменен", $"{client.Name}: {string.Join("; ", changes)}", client.Id);
            }

            await context.SaveChangesAsync();
        }, async () => savedHistoryId.HasValue && await context.OrganizationClientHistoryEvents.AsNoTracking()
            .AnyAsync(history => history.Id == savedHistoryId.Value));
    }

    /// <summary> Изменяет только комментарий клиента организации. </summary>
    public async Task UpdateCommentAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        UpdateOrganizationClientCommentDto dto)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);

        var comment = ClientRules.NormalizeComment(dto.Comment, null);
        var now = DateTime.UtcNow;
        Guid? savedHistoryId = null;

        await transaction.ExecuteAsync(async () =>
        {
            savedHistoryId = null;
            await transaction.LockOrganizationAsync(organizationId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationClients" WHERE "Id" = {clientId} AND "OrganizationId" = {organizationId} FOR UPDATE""");

            var client = await context.OrganizationClients
                .FirstOrDefaultAsync(existingClient => existingClient.Id == clientId
                    && existingClient.OrganizationId == organizationId
                    && !existingClient.IsDeleted)
                ?? throw new KeyNotFoundException("Клиент не найден.");

            await context.Entry(client).ReloadAsync();
            if (string.Equals(client.Comment, comment, StringComparison.Ordinal))
            {
                return;
            }

            client.Comment = comment;
            client.UpdatedAt = now;

            savedHistoryId = historyWriter.AddHistory(
                client.Id,
                organizationId,
                userId,
                "updated",
                "Комментарий клиента изменен",
                "Комментарий клиента обновлен.",
                null,
                null,
                now);
            await historyWriter.AddAuditAsync(
                organizationId,
                userId,
                "clients.updated",
                "Комментарий клиента изменен",
                client.Name,
                client.Id);
            await context.SaveChangesAsync();
        }, async () => savedHistoryId.HasValue
            ? await context.OrganizationClientHistoryEvents.AsNoTracking()
                .AnyAsync(history => history.Id == savedHistoryId.Value)
            : await context.OrganizationClients.AsNoTracking()
                .AnyAsync(client => client.Id == clientId
                    && client.OrganizationId == organizationId
                    && !client.IsDeleted
                    && client.Comment == comment));
    }

    /// <summary> Удаляет клиента организации. </summary>
    public Task DeleteAsync(string userId, Guid organizationId, Guid clientId) =>
        this.ArchiveAsync(userId, organizationId, clientId);

    /// <summary> Помещает клиента организации в архив. </summary>
    public async Task ArchiveAsync(string userId, Guid organizationId, Guid clientId)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsArchive);

        var client = await context.OrganizationClients
            .FirstOrDefaultAsync(existingClient => existingClient.Id == clientId
                && existingClient.OrganizationId == organizationId
                && !existingClient.IsDeleted)
            ?? throw new InvalidOperationException("Клиент не найден.");

        var now = DateTime.UtcNow;
        client.Delete(now);
        historyWriter.AddHistory(client.Id, organizationId, userId, "archived", "Клиент отправлен в архив", "Связь с техникой сохранена. Клиента можно восстановить из архива.", null, null, now);
        await historyWriter.AddAuditAsync(organizationId, userId, "clients.archived", "Клиент отправлен в архив", client.Name, client.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Возвращает клиента организации из архива. </summary>
    public async Task RestoreAsync(string userId, Guid organizationId, Guid clientId)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsArchive);

        var client = await context.OrganizationClients
            .FirstOrDefaultAsync(existingClient => existingClient.Id == clientId
                && existingClient.OrganizationId == organizationId
                && existingClient.IsDeleted)
            ?? throw new InvalidOperationException("Клиент в архиве не найден.");

        var now = DateTime.UtcNow;
        client.Restore(now);
        historyWriter.AddHistory(client.Id, organizationId, userId, "restored", "Клиент восстановлен из архива", null, null, null, now);
        await historyWriter.AddAuditAsync(organizationId, userId, "clients.restored", "Клиент восстановлен из архива", client.Name, client.Id);
        await context.SaveChangesAsync();
    }

    /// <summary> Добавляет запись в историю клиента вручную. </summary>
    public async Task AddHistoryEntryAsync(string userId, Guid organizationId, Guid clientId, string text, DateTime? occurredAt)
    {
        await clientsAccess.EnsureEnabledAsync(userId, organizationId);
        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);

        var normalizedText = ClientRules.NormalizeRequired(text, "Укажите текст записи.");
        if (normalizedText.Length > 1000)
        {
            normalizedText = normalizedText[..1000];
        }

        var clientExists = await context.OrganizationClients
            .AsNoTracking()
            .AnyAsync(client => client.Id == clientId && client.OrganizationId == organizationId && !client.IsDeleted);
        if (!clientExists)
        {
            throw new InvalidOperationException("Клиент не найден.");
        }

        var entryDate = ClientRules.NormalizeManualHistoryDate(occurredAt);
        historyWriter.AddHistory(clientId, organizationId, userId, "manual_history_entry", "Запись добавлена вручную", normalizedText, null, null, entryDate);
        await historyWriter.AddAuditAsync(organizationId, userId, "clients.history.created", "Запись истории клиента добавлена", normalizedText, clientId);
        await context.SaveChangesAsync();
    }
}
