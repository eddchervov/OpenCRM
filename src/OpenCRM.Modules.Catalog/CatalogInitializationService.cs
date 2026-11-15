using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Defaults;

namespace OpenCRM.Modules.Catalog;

/// <summary> Сервис инициализации каталога по умолчанию. </summary>
internal sealed class CatalogInitializationService(
    OpenCRMContext context) : ICatalogInitializationService
{
    /// <summary> Создаёт элементы каталога по умолчанию при их отсутствии. </summary>
    public async Task EnsureDefaultsAsync(Guid organizationId)
    {
        // Инициализация при чтении не должна сохранять чужие отслеживаемые изменения.
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(context.Database.GetDbConnection(), database => database.EnableRetryOnFailure()).Options;
        await using var initializationContext = new OpenCRMContext(options);
        if (context.Database.CurrentTransaction != null)
        {
            await initializationContext.Database.UseTransactionAsync(context.Database.CurrentTransaction.GetDbTransaction());
        }

        var defaults = new CatalogDefaultsService(initializationContext);
        var transaction = new CatalogTransaction(initializationContext);
        await transaction.ExecuteAsync(async () =>
        {
            await transaction.LockOrganizationAsync(organizationId);
            await defaults.EnsureDefaultsAsync(organizationId);

            return true;
        }, () => Task.FromResult(false));
    }
}
