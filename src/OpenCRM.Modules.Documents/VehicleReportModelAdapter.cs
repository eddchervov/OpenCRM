using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Преобразует данные техники и обслуживания в модели отчётов PDF и Word. </summary>
internal static class VehicleReportModelAdapter
{
    /// <summary> Создаёт модель шапки PDF-отчёта по технике и её фотографии. </summary>
    public static VehiclePdfModel CreatePdfHeader(GetVehicleDto vehicle, byte[]? vehiclePhoto) =>
        new()
        {
            Vehicle = new VehicleInfo
            {
                BrandName = vehicle.BrandName,
                ModelName = vehicle.ModelName,
                RegistrationPlate = vehicle.RegistrationPlate,
                MileageUnit = vehicle.MileageUnit,
                YearIssue = vehicle.YearIssue,
                Name = vehicle.Name,
                Description = vehicle.Description,
                PhotoData = vehiclePhoto
            },
            MaintenanceHistory = []
        };

    /// <summary> Создаёт модель записи обслуживания для PDF-отчёта. </summary>
    public static MaintenanceInfo CreatePdfMaintenance(GetMaintenanceDto maintenance) =>
        new()
        {
            Type = maintenance.Type,
            Date = maintenance.Date.ToString("dd.MM.yyyy"),
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = maintenance.Comment,
            Price = maintenance.Price,
            MaintenanceDetails = maintenance.MaintenanceDetails
                .Select(detail => new MaintenanceDetail
                {
                    Name = detail.Name,
                    Quantity = detail.Quantity,
                    Description = detail.Description,
                    Price = detail.Price
                })
                .ToList(),
            Photos = []
        };

    /// <summary> Создаёт модель шапки Word-отчёта по технике и истории обслуживания. </summary>
    public static WordVehicleModel CreateWordHeader(
        GetVehicleDto vehicle,
        IReadOnlyCollection<GetMaintenanceDto> maintenanceHistory,
        byte[]? vehiclePhoto) =>
        new()
        {
            Vehicle = new WordVehicleInfo
            {
                BrandName = vehicle.BrandName,
                ModelName = vehicle.ModelName,
                RegistrationPlate = vehicle.RegistrationPlate,
                MileageUnit = vehicle.MileageUnit,
                YearIssue = vehicle.YearIssue,
                Name = vehicle.Name,
                Description = vehicle.Description,
                PhotoData = vehiclePhoto
            },
            MaintenanceHistory = [],
            GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            TotalMaintenanceCount = maintenanceHistory.Count,
            TotalSpent = (decimal)maintenanceHistory.Sum(maintenance => maintenance.Price ?? 0)
        };

    /// <summary> Создаёт модель записи обслуживания для Word-отчёта. </summary>
    public static WordMaintenanceInfo CreateWordMaintenance(GetMaintenanceDto maintenance) =>
        new()
        {
            Type = (int)maintenance.Type,
            Date = maintenance.Date.ToString("dd.MM.yyyy"),
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = maintenance.Comment,
            Price = (decimal?)maintenance.Price,
            MaintenanceDetails = maintenance.MaintenanceDetails
                .Select(detail => new WordMaintenanceDetail
                {
                    Name = detail.Name,
                    Quantity = detail.Quantity,
                    Description = detail.Description,
                    Price = (decimal?)detail.Price
                })
                .ToList(),
            Photos = []
        };
}
