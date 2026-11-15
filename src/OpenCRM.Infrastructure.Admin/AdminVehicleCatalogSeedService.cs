using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис начального наполнения справочника техники. </summary>
public sealed class AdminVehicleCatalogSeedService(OpenCRMContext opencrmContext) : IAdminVehicleCatalogSeedService
{
    /// <summary> Имя встроенного ресурса с данными справочника. </summary>
    private const string SeedResourceName = "OpenCRM.Infrastructure.Admin.VehicleCatalog.vehicle-catalog-seed.json";

    /// <summary> Ключ advisory-блокировки наполнения справочника. </summary>
    private const long CatalogSeedAdvisoryLockKey = 0x4D4F544F52495354;

    /// <summary> Статические данные справочника по видам техники. </summary>
    private static readonly IReadOnlyList<VehicleCatalogSeedKind> s_seed = AdminVehicleCatalogSeedService.LoadSeed();

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Наполняет справочник техники данными из встроенного ресурса. </summary>
    public async Task<AdminVehicleCatalogSeedResultDto> SeedAsync(CancellationToken cancellationToken = default)
    {
        var isRetry = false;

        return await this._opencrmContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (isRetry)
            {
                this._opencrmContext.ChangeTracker.Clear();
            }

            isRetry = true;

            await using var transaction = await this._opencrmContext.Database
                .BeginTransactionAsync(cancellationToken);
            await this._opencrmContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdminVehicleCatalogSeedService.CatalogSeedAdvisoryLockKey})",
                cancellationToken);

            var brands = await this._opencrmContext.Brands
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            var models = await this._opencrmContext.Models
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            var createdBrands = 0;
            var createdModels = 0;
            var skippedDeletedBrands = 0;
            var skippedDeletedModels = 0;

            foreach (var kindSeed in AdminVehicleCatalogSeedService.s_seed)
            {
                foreach (var brandSeed in kindSeed.Brands)
                {
                    var brand = brands.FirstOrDefault(item =>
                        item.Kind == kindSeed.Kind &&
                        string.Equals(item.Name, brandSeed.Name, StringComparison.OrdinalIgnoreCase));

                    if (brand?.IsDeleted == true)
                    {
                        skippedDeletedBrands++;
                        skippedDeletedModels += brandSeed.Models.Count;
                        continue;
                    }

                    if (brand is null)
                    {
                        brand = new Brand
                        {
                            Name = brandSeed.Name,
                            LogoKey = brandSeed.LogoKey,
                            Kind = kindSeed.Kind
                        };

                        this._opencrmContext.Brands.Add(brand);
                        brands.Add(brand);
                        createdBrands++;
                        await this._opencrmContext.SaveChangesAsync(cancellationToken);
                    }

                    foreach (var modelName in brandSeed.Models)
                    {
                        var model = models.FirstOrDefault(item =>
                            item.BrandId == brand.Id &&
                            item.Kind == kindSeed.Kind &&
                            string.Equals(item.Name, modelName, StringComparison.OrdinalIgnoreCase));

                        if (model is not null)
                        {
                            if (model.IsDeleted)
                            {
                                skippedDeletedModels++;
                            }

                            continue;
                        }

                        var newModel = new Model
                        {
                            Name = modelName,
                            BrandId = brand.Id,
                            Kind = kindSeed.Kind
                        };

                        this._opencrmContext.Models.Add(newModel);
                        models.Add(newModel);
                        createdModels++;
                    }
                }
            }

            await this._opencrmContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AdminVehicleCatalogSeedResultDto
            {
                CreatedBrands = createdBrands,
                CreatedModels = createdModels,
                SkippedDeletedBrands = skippedDeletedBrands,
                SkippedDeletedModels = skippedDeletedModels
            };
        });
    }

    /// <summary> Загружает данные справочника из ресурса. </summary>
    private static IReadOnlyList<VehicleCatalogSeedKind> LoadSeed()
    {
        using var stream = typeof(AdminVehicleCatalogSeedService).Assembly.GetManifestResourceStream(
            AdminVehicleCatalogSeedService.SeedResourceName)!;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return JsonSerializer.Deserialize<List<VehicleCatalogSeedKind>>(stream, options)!;
    }

    /// <summary> Вид техники со списком марок для наполнения. </summary>
    private sealed record VehicleCatalogSeedKind(VehicleKind Kind, List<VehicleCatalogSeedBrand> Brands);

    /// <summary> Марка техники со списком моделей. </summary>
    private sealed record VehicleCatalogSeedBrand(string Name, List<string> Models, string? LogoKey);
}
