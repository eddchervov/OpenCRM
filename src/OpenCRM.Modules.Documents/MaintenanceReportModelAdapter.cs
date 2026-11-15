using OpenCRM.Services.Interfaces.Documents.Dtos;
using Helpers;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Адаптер моделей данных для отчётов по обслуживанию. </summary>
internal static class MaintenanceReportModelAdapter
{
    /// <summary> Строит модель PDF-отчёта по обслуживанию. </summary>
    public static MaintenancePdfModel CreatePdf(
        MaintenanceReportData data,
        byte[]? vehiclePhoto, string projectName)
    {
        var maintenance = data.Maintenance;
        var vehicle = data.Vehicle!;

        return new MaintenancePdfModel
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
            Maintenance = new MaintenanceInfo
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
                IntakeNotes = maintenance.IntakeNotes,
                Photos = []
            },
            CompanyInfo = $"{projectName} - Ваш надежный помощник по технике"
        };
    }

    /// <summary> Строит модель Word-отчёта по обслуживанию. </summary>
    public static WordMaintenanceModel CreateWord(MaintenanceReportData data, byte[]? vehiclePhoto, string projectName)
    {
        var maintenance = data.Maintenance;
        var vehicle = data.Vehicle!;

        return new WordMaintenanceModel
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
            Maintenance = new WordMaintenanceInfo
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
            },
            GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            CompanyInfo = $"{projectName} - Ваш надежный помощник по технике"
        };
    }
}
