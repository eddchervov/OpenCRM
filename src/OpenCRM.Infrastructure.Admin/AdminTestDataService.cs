using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис генерации тестовых данных. </summary>
public sealed class AdminTestDataService(OpenCRMContext opencrmContext) : IAdminTestDataService
{
    /// <summary> Максимальное количество генерируемых единиц техники. </summary>
    private const int MaxVehicleGenerateCount = 100;

    /// <summary> Максимальное количество генерируемых обслуживаний. </summary>
    private const int MaxMaintenanceGenerateCount = 300;

    /// <summary> Максимальное количество генерируемых дефектов. </summary>
    private const int MaxDefectGenerateCount = 100;

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Ищет организации для генерации тестовых данных. </summary>
    public async Task<IReadOnlyList<AdminTestDataOrganizationDto>> SearchOrganizationsAsync(string? search)
    {
        var query = this.BuildOrganizationsQuery(search);

        return await this.ProjectOrganizations(query
                .OrderByDescending(organization => organization.CreatedAt)
                .Take(25))
            .ToListAsync();
    }

    /// <summary> Возвращает страницу организаций с поиском по организации и владельцу. </summary>
    public async Task<AdminTestDataOrganizationsPageDto> SearchOrganizationsPageAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = this.BuildOrganizationsQuery(search);
        var totalCount = await query.CountAsync(cancellationToken);
        var normalizedPageSize = Math.Clamp(pageSize, 10, 100);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)normalizedPageSize));
        var normalizedPage = Math.Clamp(page, 1, pageCount);
        var items = await this.ProjectOrganizations(query)
            .OrderBy(organization => organization.Name)
            .ThenBy(organization => organization.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new AdminTestDataOrganizationsPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = normalizedPage,
            PageSize = normalizedPageSize
        };
    }

    /// <summary> Возвращает одну организацию без загрузки списка. </summary>
    public async Task<AdminTestDataOrganizationDto?> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        return await this.ProjectOrganizations(this._opencrmContext.Organizations
                .Where(organization => organization.Id == organizationId && !organization.IsDeleted))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<Organization> BuildOrganizationsQuery(string? search)
    {
        var query = this._opencrmContext.Organizations
            .AsNoTracking()
            .Where(organization => !organization.IsDeleted);
        var normalizedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return query;
        }

        return query.Where(organization =>
            EF.Functions.ILike(organization.Name, $"%{normalizedSearch}%") ||
            (organization.Email != null && EF.Functions.ILike(organization.Email, $"%{normalizedSearch}%")) ||
            (organization.OwnerUser.Email != null && EF.Functions.ILike(organization.OwnerUser.Email, $"%{normalizedSearch}%")) ||
            organization.OwnerUserId == normalizedSearch ||
            organization.Id.ToString() == normalizedSearch);
    }

    private IQueryable<AdminTestDataOrganizationDto> ProjectOrganizations(IQueryable<Organization> query)
    {
        return query.Select(organization => new AdminTestDataOrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            OwnerUserId = organization.OwnerUserId,
            OwnerEmail = organization.OwnerUser.Email,
            ScenarioCode = organization.PrimaryScenarioCode,
            IsActive = organization.IsActive && !organization.IsArchived,
            VehiclesCount = this._opencrmContext.Vehicles.Count(vehicle =>
                vehicle.OrganizationId == organization.Id && !vehicle.IsDeleted),
            MaintenanceCount = this._opencrmContext.Vehicles
                .Where(vehicle => vehicle.OrganizationId == organization.Id && !vehicle.IsDeleted)
                .SelectMany(vehicle => vehicle.Maintenance)
                .Count(maintenance => !maintenance.IsDeleted)
        });
    }

    /// <summary> Возвращает технику указанной организации. </summary>
    public async Task<AdminTestDataVehiclesResultDto> GetVehiclesAsync(Guid organizationId)
    {
        var organizationExists = await this._opencrmContext.Organizations
            .AnyAsync(organization => organization.Id == organizationId && !organization.IsDeleted);
        if (!organizationExists)
        {
            return new AdminTestDataVehiclesResultDto
            {
                NotFound = true,
                ErrorMessage = "Организация не найдена."
            };
        }

        var vehicles = await this._opencrmContext.Vehicles
            .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted)
            .OrderByDescending(vehicle => vehicle.DateTimeCreation)
            .Select(vehicle => new AdminTestDataVehicleDto
            {
                Id = vehicle.Id,
                Kind = vehicle.Kind,
                BrandName = vehicle.BrandId.HasValue
                    ? vehicle.Brand != null ? vehicle.Brand.Name : string.Empty
                    : vehicle.ManualBrandName ?? string.Empty,
                ModelName = vehicle.ModelId.HasValue
                    ? vehicle.Model != null ? vehicle.Model.Name : string.Empty
                    : vehicle.ManualModelName ?? string.Empty,
                DisplayName = Helpers.VehicleDisplayName.Format(
                    vehicle.Brand != null ? vehicle.Brand.Name : vehicle.ManualBrandName,
                    vehicle.Model != null ? vehicle.Model.Name : vehicle.ManualModelName, vehicle.RegistrationPlate),
                Name = vehicle.Name,
                YearIssue = vehicle.YearIssue,
                IsArchived = vehicle.IsArchived,
                MaintenanceCount = vehicle.Maintenance.Count(maintenance => !maintenance.IsDeleted),
                DefectCount = vehicle.Defects.Count(defect => !defect.IsDeleted),
                OrganizationId = vehicle.OrganizationId,
                OrganizationName = vehicle.Organization != null ? vehicle.Organization.Name : null
            })
            .ToListAsync();

        return new AdminTestDataVehiclesResultDto
        {
            Vehicles = vehicles
        };
    }

    /// <summary> Генерирует тестовые записи техники. </summary>
    public async Task<AdminTestDataGenerationResultDto> GenerateVehiclesAsync(Guid organizationId, int count)
    {
        var normalizedCount = NormalizeCount(count, 1, MaxVehicleGenerateCount);
        var organization = await this._opencrmContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == organizationId && !item.IsDeleted);
        if (organization == null)
        {
            return Missing("Организация не найдена.");
        }

        var brands = await this._opencrmContext.Brands.Where(brand => !brand.IsDeleted).ToListAsync();
        var models = await this._opencrmContext.Models.Where(model => !model.IsDeleted).ToListAsync();

        var catalog = brands
            .Select(brand => new
            {
                Brand = brand,
                Models = models.Where(model => model.BrandId == brand.Id && model.Kind == brand.Kind).ToList()
            })
            .Where(item => item.Models.Count > 0)
            .ToList();

        if (catalog.Count == 0)
        {
            return Invalid("В справочнике нет брендов с моделями.");
        }

        var random = new Random();
        var now = DateTime.UtcNow;
        var vehicles = new List<Vehicle>();

        for (var i = 0; i < normalizedCount; i++)
        {
            var item = catalog[random.Next(catalog.Count)];
            var model = item.Models[random.Next(item.Models.Count)];

            vehicles.Add(new Vehicle
            {
                Id = Guid.NewGuid(),
                Kind = item.Brand.Kind,
                BrandId = item.Brand.Id,
                ModelId = model.Id,
                YearIssue = random.Next(1990, DateTime.UtcNow.Year + 1),
                Name = $"Тестовая техника {i + 1}",
                Description = $"Техника для тестирования #{i + 1}",
                UserId = organization.OwnerUserId,
                OrganizationId = organization.Id,
                DateTimeCreation = now.AddDays(-random.Next(0, 365)),
                IsDeleted = false,
                IsArchived = false
            });
        }

        await this._opencrmContext.Vehicles.AddRangeAsync(vehicles);
        await this._opencrmContext.SaveChangesAsync();

        return Created("техники", vehicles.Count);
    }

    /// <summary> Генерирует тестовые записи обслуживаний для техники. </summary>
    public async Task<AdminTestDataGenerationResultDto> GenerateMaintenancesAsync(
        Guid organizationId,
        Guid vehicleId,
        int count,
        string? mode)
    {
        var normalizedCount = NormalizeCount(count, 1, MaxMaintenanceGenerateCount);
        var vehicle = await this._opencrmContext.Vehicles
            .FirstOrDefaultAsync(item =>
                item.Id == vehicleId &&
                item.OrganizationId == organizationId &&
                !item.IsDeleted);

        if (vehicle == null)
        {
            return Missing("Техника организации не найдена.");
        }

        var maintenances = CreateTestMaintenances(vehicle.UserId, vehicle.Id, normalizedCount, mode);
        await this._opencrmContext.Maintenance.AddRangeAsync(maintenances);
        await this._opencrmContext.SaveChangesAsync();

        return Created("обслуживаний", maintenances.Count);
    }

    /// <summary> Генерирует тестовые записи дефектов для техники. </summary>
    public async Task<AdminTestDataGenerationResultDto> GenerateDefectsAsync(
        Guid organizationId,
        Guid vehicleId,
        int count)
    {
        var normalizedCount = NormalizeCount(count, 1, MaxDefectGenerateCount);
        var vehicle = await this._opencrmContext.Vehicles
            .FirstOrDefaultAsync(item =>
                item.Id == vehicleId &&
                item.OrganizationId == organizationId &&
                !item.IsDeleted);

        if (vehicle == null)
        {
            return Missing("Техника организации не найдена.");
        }

        var defects = CreateTestDefects(vehicle.UserId, organizationId, vehicle.Id, normalizedCount);
        await this._opencrmContext.VehicleDefects.AddRangeAsync(defects);
        await this._opencrmContext.SaveChangesAsync();

        return Created("неисправностей", defects.Count);
    }

    /// <summary> Приводит количество к допустимому диапазону. </summary>
    private static int NormalizeCount(int count, int min, int max)
    {
        if (count < min)
        {
            return min;
        }

        return count > max ? max : count;
    }

    /// <summary> Формирует список тестовых обслуживаний. </summary>
    private static List<Maintenance> CreateTestMaintenances(
        string userId,
        Guid vehicleId,
        int count,
        string? mode)
    {
        var random = new Random();
        var isPlanned = string.Equals(mode, "planned", StringComparison.OrdinalIgnoreCase);
        var locations = new[]
        {
            "ООО АВТОСЕРВИС", "СТО МАСТЕР", "АВТОЦЕНТР СПЕКТР", "ТЕХЦЕНТР ВОЛГА",
            "СЕРВИС ПЛЮС", "ТЕХНОСЕРВИС", "АВТОДОКТОР", "ЛУКОЙЛ", "ГАЗПРОМНЕФТЬ"
        };
        var cityParts = new[] { "Северный", "Южный", "Восточный", "Западный", "Центральный" };
        var servicesByType = new Dictionary<MaintenanceType, string[]>
        {
            [MaintenanceType.Maintenance] =
            [
                "Замена масла двигателя", "Замена масляного фильтра", "Замена воздушного фильтра",
                "Замена салонного фильтра", "Замена топливного фильтра", "Замена свечей зажигания",
                "Замена ремня ГРМ", "Замена антифриза", "Регулировка развала-схождения"
            ],
            [MaintenanceType.Repair] =
            [
                "Замена тормозных колодок", "Замена тормозных дисков", "Ремонт ходовой части",
                "Замена амортизаторов", "Ремонт рулевой рейки", "Замена ШРУСа",
                "Ремонт выхлопной системы", "Замена сцепления", "Ремонт КПП"
            ],
            [MaintenanceType.Refill] =
            [
                "Заправка кондиционера", "Замена жидкости ГУР", "Замена жидкости стеклоомывателя",
                "Долив антифриза", "Заправка топливной системы", "Заправка AdBlue"
            ],
            [MaintenanceType.Other] =
            [
                "Чистка салона", "Полировка кузова", "Установка сигнализации",
                "Установка аудиосистемы", "Тонировка стекол", "Химчистка"
            ]
        };
        var commentsByType = new Dictionary<MaintenanceType, string[]>
        {
            [MaintenanceType.Maintenance] = ["Плановое ТО", "Регламентное обслуживание", "Сезонное обслуживание"],
            [MaintenanceType.Repair] = ["Внеплановый ремонт", "Устранение неисправности", "Срочный ремонт"],
            [MaintenanceType.Refill] = ["Заправка систем", "Обслуживание жидкостей", "Подготовка к сезону"],
            [MaintenanceType.Other] = ["Дополнительные услуги", "Комфорт и уход", "Тюнинг и доработки"]
        };
        var types = new[]
        {
            MaintenanceType.Maintenance,
            MaintenanceType.Repair,
            MaintenanceType.Refill,
            MaintenanceType.Other
        };
        var result = new List<Maintenance>();

        for (var i = 0; i < count; i++)
        {
            var randomDate = isPlanned
                ? DateTime.UtcNow.Date.AddDays(random.Next(-10, 46)).AddHours(random.Next(8, 19))
                : DateTime.UtcNow.AddDays(-random.Next(730));
            var maintenanceType = types[random.Next(types.Length)];
            var services = servicesByType[maintenanceType];
            var serviceCount = random.Next(1, Math.Min(4, services.Length) + 1);
            var usedServices = new HashSet<string>();

            var maintenance = new Maintenance
            {
                Id = Guid.NewGuid(),
                Status = isPlanned ? MaintenanceStatus.Planned : MaintenanceStatus.Completed,
                Date = randomDate,
                PlannedDate = isPlanned ? randomDate.Date : null,
                PlannedStartTime = isPlanned ? randomDate.TimeOfDay : null,
                PlannedEndTime = isPlanned ? randomDate.AddHours(random.Next(1, 4)).TimeOfDay : null,
                CompletedDate = isPlanned ? null : randomDate,
                DateTimeCreation = randomDate,
                Location = $"{locations[random.Next(locations.Length)]} {cityParts[random.Next(cityParts.Length)]}",
                VehicleId = vehicleId,
                Comment = isPlanned
                    ? $"Плановая запись для проверки планировщика #{i + 1}"
                    : $"{commentsByType[maintenanceType][random.Next(commentsByType[maintenanceType].Length)]} #{i + 1}",
                Mileage = random.Next(5000, 150000),
                Type = maintenanceType,
                UserId = userId,
                Details = [],
                IsDeleted = false
            };

            for (var j = 0; j < serviceCount; j++)
            {
                string serviceName;
                do
                {
                    serviceName = services[random.Next(services.Length)];
                } while (usedServices.Contains(serviceName));

                usedServices.Add(serviceName);

                maintenance.Details.Add(new MaintenanceDetail
                {
                    Position = maintenance.Details.Count,
                    Name = serviceName,
                    Description = $"Выполнена {serviceName.ToLower()}. Качество работ: отличное.",
                    Price = random.Next(500, 8000)
                });
            }

            result.Add(maintenance);
        }

        return result;
    }

    /// <summary> Формирует список тестовых дефектов. </summary>
    private static List<VehicleDefect> CreateTestDefects(
        string userId,
        Guid organizationId,
        Guid vehicleId,
        int count)
    {
        var random = new Random();
        var now = DateTime.UtcNow;
        var titles = new[]
        {
            "Стук в передней подвеске",
            "Посторонний шум при торможении",
            "Горит индикатор двигателя",
            "Течь технической жидкости",
            "Слабый заряд аккумулятора",
            "Неравномерная работа двигателя",
            "Вибрация на скорости",
            "Проблема с освещением",
            "Ошибки датчиков",
            "Повреждение кузовного элемента"
        };
        var severities = new[]
        {
            VehicleDefectSeverityCodes.Low,
            VehicleDefectSeverityCodes.Medium,
            VehicleDefectSeverityCodes.High,
            VehicleDefectSeverityCodes.Critical
        };
        var statuses = new[]
        {
            VehicleDefectStatusCodes.New,
            VehicleDefectStatusCodes.New,
            VehicleDefectStatusCodes.InProgress,
            VehicleDefectStatusCodes.Deferred
        };
        var result = new List<VehicleDefect>();

        for (var i = 0; i < count; i++)
        {
            var createdAt = now.AddDays(-random.Next(0, 45)).AddHours(-random.Next(0, 12));
            var title = titles[random.Next(titles.Length)];

            result.Add(new VehicleDefect
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                VehicleId = vehicleId,
                UserId = userId,
                Title = $"{title} #{i + 1}",
                Description = $"Тестовая неисправность для проверки планировщика. Сценарий: {title.ToLowerInvariant()}.",
                SeverityCode = severities[random.Next(severities.Length)],
                StatusCode = statuses[random.Next(statuses.Length)],
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                IsDeleted = false
            });
        }

        return result;
    }

    /// <summary> Возвращает результат с ошибкой отсутствия данных. </summary>
    private static AdminTestDataGenerationResultDto Missing(string message)
    {
        return new AdminTestDataGenerationResultDto
        {
            NotFound = true,
            ErrorMessage = message
        };
    }

    /// <summary> Возвращает результат с ошибкой некорректных данных. </summary>
    private static AdminTestDataGenerationResultDto Invalid(string message)
    {
        return new AdminTestDataGenerationResultDto
        {
            ErrorMessage = message
        };
    }

    /// <summary> Возвращает результат успешного создания записей. </summary>
    private static AdminTestDataGenerationResultDto Created(string entityName, int count)
    {
        return new AdminTestDataGenerationResultDto
        {
            Succeeded = true,
            Message = $"Создано {entityName}: {count}.",
            CreatedCount = count
        };
    }
}
