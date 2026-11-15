using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Преобразует данные обслуживания в модели PDF и Word для документов сценария. </summary>
internal static class ScenarioDocumentModelAdapter
{
    /// <summary> Создаёт модель PDF-документа сценария из данных организации, техники и обслуживания. </summary>
    public static ScenarioMaintenanceDocumentPdfModel CreatePdf(
        OrganizationDto organization,
        GetVehicleDto vehicle,
        GetMaintenanceByIdDto maintenance,
        string documentKindCode,
        string title,
        string documentNumber,
        DateTime documentDate)
    {
        var executorName = MaintenanceDocumentFacts.GetExecutorName(organization);

        return new ScenarioMaintenanceDocumentPdfModel
        {
            DocumentStyleCode = organization.DocumentStyleCode,
            ScenarioCode = organization.PrimaryScenarioCode,
            DocumentKindCode = documentKindCode,
            DocumentTitle = title,
            DocumentNumber = documentNumber,
            DocumentDate = documentDate.ToString("dd.MM.yyyy"),
            GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            ExecutorName = executorName,
            ExecutorInn = organization.Inn,
            ExecutorKpp = organization.Kpp,
            ExecutorOgrn = organization.Ogrn,
            ExecutorAddress = organization.LegalAddress ?? organization.ActualAddress,
            ExecutorPhone = organization.Phone,
            ExecutorEmail = organization.Email,
            ExecutorSignatoryPosition = organization.SignatoryPosition ?? "Исполнитель",
            ExecutorSignatoryName = organization.SignatoryName ?? executorName,
            CustomerName = vehicle.OrganizationClientName ?? string.Empty,
            VehicleTitle = MaintenanceDocumentFacts.GetVehicleTitle(vehicle.BrandName, vehicle.ModelName, vehicle.Name, vehicle.RegistrationPlate),
            VehicleRegistrationPlate = vehicle.RegistrationPlate,
            VehicleVin = vehicle.Vin,
            VehicleYear = vehicle.YearIssue?.ToString(),
            MaintenanceDate = documentDate.ToString("dd.MM.yyyy"),
            PlannedPeriod = MaintenanceDocumentFacts.FormatPlannedPeriod(maintenance),
            MaintenanceType = MaintenanceDocumentFacts.GetMaintenanceTypeText((int)maintenance.Type),
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            MileageLabel = MaintenanceDocumentFacts.GetMileageLabel(vehicle.MileageUnit),
            MileageUnit = MaintenanceDocumentFacts.GetMileageUnit(vehicle.MileageUnit),
            Comment = maintenance.Comment,
            GrossAmount = maintenance.GrossAmount,
            DiscountAmount = maintenance.DiscountAmount,
            PaidAmount = maintenance.PaidAmount,
            DebtAmount = maintenance.DebtAmount,
            MaterialCostAmount = maintenance.MaterialCostAmount,
            LaborCostAmount = maintenance.LaborCostAmount,
            ProfitAmount = maintenance.ProfitAmount,
            PaymentStatus = maintenance.PaymentStatus,
            Works = maintenance.MaintenanceDetails
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
    }

    /// <summary> Создаёт модель Word-документа сценария из данных организации, техники и обслуживания. </summary>
    public static WordScenarioMaintenanceDocumentModel CreateWord(
        OrganizationDto organization,
        GetVehicleDto vehicle,
        GetMaintenanceByIdDto maintenance,
        string documentKindCode,
        string title,
        string documentNumber,
        DateTime documentDate)
    {
        var executorName = MaintenanceDocumentFacts.GetExecutorName(organization);

        return new WordScenarioMaintenanceDocumentModel
        {
            DocumentStyleCode = organization.DocumentStyleCode,
            ScenarioCode = organization.PrimaryScenarioCode,
            DocumentKindCode = documentKindCode,
            DocumentTitle = title,
            DocumentNumber = documentNumber,
            DocumentDate = documentDate.ToString("dd.MM.yyyy"),
            GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            ExecutorName = executorName,
            ExecutorInn = organization.Inn,
            ExecutorKpp = organization.Kpp,
            ExecutorOgrn = organization.Ogrn,
            ExecutorAddress = organization.LegalAddress ?? organization.ActualAddress,
            ExecutorPhone = organization.Phone,
            ExecutorEmail = organization.Email,
            ExecutorSignatoryPosition = organization.SignatoryPosition ?? "Исполнитель",
            ExecutorSignatoryName = organization.SignatoryName ?? executorName,
            CustomerName = vehicle.OrganizationClientName ?? string.Empty,
            VehicleTitle = MaintenanceDocumentFacts.GetVehicleTitle(vehicle.BrandName, vehicle.ModelName, vehicle.Name, vehicle.RegistrationPlate),
            VehicleRegistrationPlate = vehicle.RegistrationPlate,
            VehicleVin = vehicle.Vin,
            VehicleYear = vehicle.YearIssue?.ToString(),
            MaintenanceDate = documentDate.ToString("dd.MM.yyyy"),
            PlannedPeriod = MaintenanceDocumentFacts.FormatPlannedPeriod(maintenance),
            MaintenanceType = MaintenanceDocumentFacts.GetMaintenanceTypeText((int)maintenance.Type),
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            MileageLabel = MaintenanceDocumentFacts.GetMileageLabel(vehicle.MileageUnit),
            MileageUnit = MaintenanceDocumentFacts.GetMileageUnit(vehicle.MileageUnit),
            Comment = maintenance.Comment,
            GrossAmount = maintenance.GrossAmount,
            DiscountAmount = maintenance.DiscountAmount,
            PaidAmount = maintenance.PaidAmount,
            DebtAmount = maintenance.DebtAmount,
            MaterialCostAmount = maintenance.MaterialCostAmount,
            LaborCostAmount = maintenance.LaborCostAmount,
            ProfitAmount = maintenance.ProfitAmount,
            PaymentStatus = maintenance.PaymentStatus,
            Works = maintenance.MaintenanceDetails
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
}
