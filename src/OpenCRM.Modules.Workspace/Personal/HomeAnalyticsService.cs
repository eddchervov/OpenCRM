using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Modules.Workspace.Personal;

/// <summary> Сервис аналитики для домашней страницы пользователя. </summary>
public class HomeAnalyticsService(OpenCRMContext opencrmContext) : IHomeAnalyticsService
{
    /// <summary> Возвращает аналитические данные для домашней страницы. </summary>
    public async Task<HomeAnalyticsDto> GetAsync(string userId, int vehicleCount)
    {
        var maintenance = await opencrmContext.Maintenance
            .AsNoTracking()
            .Include(x => x.Vehicle)
            .ThenInclude(x => x.Brand)
            .Include(x => x.Vehicle)
            .ThenInclude(x => x.Model)
            .Where(x => x.UserId == userId && x.Vehicle.OrganizationId == null
                && !x.IsDeleted && !x.Vehicle.IsDeleted && !x.Vehicle.IsArchived)
            .ToListAsync();
        var currencyCode = await opencrmContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PersonalCurrencyCode)
            .SingleAsync();

        var pricedMaintenance = maintenance
            .Select(x => new
            {
                Maintenance = x,
                Price = x.Details.Where(d => d.Price.HasValue).Sum(d => d.Price!.Value)
            })
            .ToList();

        var personalMaintenance = pricedMaintenance.Where(item => item.Maintenance.CurrencyCode == currencyCode).ToList();
        var otherCurrencyTotals = pricedMaintenance
            .Where(item => item.Maintenance.CurrencyCode != currencyCode)
            .GroupBy(item => item.Maintenance.CurrencyCode)
            .Select(group => new HomeAnalyticsCurrencyTotalDto
            {
                CurrencyCode = group.Key,
                TotalSpent = group.Sum(item => item.Price),
                MaintenanceCount = group.Count(item => item.Price > 0)
            })
            .OrderBy(total => total.CurrencyCode)
            .ToList();
        var totalSpent = personalMaintenance.Sum(item => item.Price);
        var paidMaintenanceCount = personalMaintenance.Count(item => item.Price > 0);

        var monthlyCosts = personalMaintenance
            .Where(x => x.Price > 0)
            .GroupBy(x => new DateTime(x.Maintenance.Date.Year, x.Maintenance.Date.Month, 1))
            .OrderBy(x => x.Key)
            .TakeLast(6)
            .Select(x => new HomeAnalyticsMonthItemDto
            {
                Label = x.Key.ToString("MM.yy"),
                Total = x.Sum(item => item.Price)
            })
            .ToList();

        var maxMonthlyCost = monthlyCosts.Count > 0 ? monthlyCosts.Max(x => x.Total) : 0;
        foreach (var item in monthlyCosts)
        {
            item.Percent = maxMonthlyCost > 0
                ? Math.Max(8, (int)Math.Round(item.Total / maxMonthlyCost * 100))
                : 0;
        }

        var topVehicle = personalMaintenance
            .Where(x => x.Price > 0)
            .GroupBy(x => x.Maintenance.Vehicle)
            .Select(x => new
            {
                Vehicle = x.Key,
                Total = x.Sum(item => item.Price)
            })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        return new HomeAnalyticsDto
        {
            VehicleCount = vehicleCount,
            MaintenanceCount = maintenance.Count,
            TotalSpent = totalSpent,
            CurrencyCode = currencyCode,
            OtherCurrencyTotals = otherCurrencyTotals,
            AverageMaintenanceCost = paidMaintenanceCount > 0 ? totalSpent / paidMaintenanceCount : 0,
            LastMaintenanceDate = maintenance.Count > 0 ? maintenance.Max(x => x.Date) : null,
            TopVehicleName = topVehicle == null ? null : BuildVehicleName(topVehicle.Vehicle),
            TopVehicleSpent = topVehicle?.Total ?? 0,
            TypeStats = maintenance
                .GroupBy(x => x.Type)
                .OrderByDescending(x => x.Count())
                .Select(x => new HomeAnalyticsTypeItemDto
                {
                    Name = GetMaintenanceTypeText(x.Key),
                    Count = x.Count(),
                    Percent = maintenance.Count > 0 ? (int)Math.Round((double)x.Count() / maintenance.Count * 100) : 0
                })
                .ToList(),
            MonthlyCosts = monthlyCosts
        };
    }

    /// <summary> Строит отображаемое название техники. </summary>
    private static string BuildVehicleName(Vehicle vehicle)
    {
        var brand = vehicle.Brand?.Name ?? vehicle.ManualBrandName;
        var model = vehicle.Model?.Name ?? vehicle.ManualModelName;
        return Helpers.VehicleDisplayName.Format(brand, model, vehicle.RegistrationPlate);
    }

    /// <summary> Возвращает текст типа обслуживания. </summary>
    private static string GetMaintenanceTypeText(MaintenanceType type) => type switch
    {
        MaintenanceType.Maintenance => "Ремонт и обслуживание",
        MaintenanceType.Repair => "Плановое ТО",
        MaintenanceType.Refill => "Заправка топливом",
        MaintenanceType.Other => "Другое",
        _ => "Обслуживание"
    };
}
