using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

public sealed partial class OrganizationImportService
{
    public async Task<IReadOnlyList<ImportProfileDto>> GetProfilesAsync(
        string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var profiles = await context.Set<OrganizationImportProfile>().AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && profile.UserId == userId)
            .OrderByDescending(profile => profile.UpdatedAt).ToListAsync(cancellationToken);

        return profiles.Select(profile =>
        {
            var result = JsonSerializer.Deserialize<ImportProfileDto>(profile.SettingsJson)!;
            result.Id = profile.Id;
            result.UpdatedAt = profile.UpdatedAt;

            return result;
        }).ToList();
    }

    public async Task<ImportProfileDto> SaveProfileAsync(
        string userId, Guid organizationId, ImportProfileDto profile, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var name = profile.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsControl))
        {
            throw new InvalidDataException("Укажите название профиля до 100 символов.");
        }

        if (profile.Tables == null || profile.Mappings == null || profile.Tables.Count is 0 or > 100 ||
            profile.Tables.Any(table => table == null || table.Columns == null || table.Columns.Count is 0 or > 100 ||
                table.Columns.Any(column => column == null || column.Length > 1000) || string.IsNullOrWhiteSpace(table.Id) ||
                table.Id.Length > 100 || table.Name == null || table.Name.Length > 260))
        {
            throw new InvalidDataException("Проверьте структуру таблиц профиля.");
        }

        var saved = new ImportProfileDto
        {
            Name = name,
            SourceKey = NormalizeSourceKey(profile.SourceKey),
            Tables = profile.Tables.Select(table => new ImportSourceTable
            {
                Id = table.Id, Name = table.Name, Columns = table.Columns.ToList()
            }).ToList(),
            Mappings = profile.Mappings
        };
        ImportMappingBuilder.Build(saved.Tables, saved.Mappings);
        var json = JsonSerializer.Serialize(saved);
        if (json.Length > 200_000)
        {
            throw new InvalidDataException("Настройки профиля превышают 200000 символов.");
        }

        // Профили принадлежат автору: настройки могут содержать его словари значений.
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
            var profiles = context.Set<OrganizationImportProfile>().Where(item => item.OrganizationId == organizationId && item.UserId == userId);
            var entity = await profiles.SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
            if (entity == null)
            {
                if (await profiles.CountAsync(cancellationToken) >= 50)
                {
                    throw new InvalidDataException("Можно сохранить до 50 профилей. Удалите ненужный профиль.");
                }

                entity = new OrganizationImportProfile { OrganizationId = organizationId, UserId = userId };
                context.Add(entity);
            }

            entity.Name = name;
            entity.SourceKey = saved.SourceKey;
            entity.SettingsJson = json;
            entity.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            saved.Id = entity.Id;
            saved.UpdatedAt = entity.UpdatedAt;

            return saved;
        });
    }

    public async Task DeleteProfileAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var entity = await context.Set<OrganizationImportProfile>()
            .SingleOrDefaultAsync(profile => profile.Id == id && profile.OrganizationId == organizationId && profile.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException();
        context.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ImportDraftDto> SetSourceAsync(
        string userId, Guid organizationId, Guid id, int revision, string? sourceKey, CancellationToken cancellationToken)
    {
        await this.EnsureAccessAsync(userId, organizationId);
        var draft = await this.LoadAsync(organizationId, id, cancellationToken);
        await this.EnsureDraftReadAccessAsync(userId, draft);
        EnsureBatchEditable(draft, revision);
        if (draft.ProcessedRows > 0)
        {
            throw new InvalidDataException("После начала переноса источник CRM изменять нельзя.");
        }

        draft.SourceKey = NormalizeSourceKey(sourceKey);
        this.Queue(draft, "validation-queued", userId);
        await context.SaveChangesAsync(cancellationToken);

        return await this.GetPageAsync(userId, organizationId, id, 1, 50, null, null, null, cancellationToken);
    }

    private static string? NormalizeSourceKey(string? sourceKey)
    {
        var value = sourceKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > 100 || value.Any(char.IsControl))
        {
            throw new InvalidDataException("Укажите постоянное имя базы исходной CRM до 100 символов.");
        }

        return value;
    }
}
