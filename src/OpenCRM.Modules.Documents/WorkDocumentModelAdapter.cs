using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Преобразует данные обслуживания в модели актов и заказ-нарядов PDF и Word. </summary>
internal static class WorkDocumentModelAdapter
{
    /// <summary> Создаёт модель PDF-акта по данным организации, техники и обслуживания. </summary>
    public static MaintenanceWorkActPdfModel CreatePdf(
        OrganizationDto organization,
        GetVehicleDto vehicle,
        GetMaintenanceByIdDto maintenance,
        DateTime documentDate,
        string documentNumber)
    {
        var executorName = MaintenanceDocumentFacts.GetExecutorName(organization);

        return new MaintenanceWorkActPdfModel
        {
            DocumentStyleCode = organization.DocumentStyleCode,
            ScenarioCode = organization.PrimaryScenarioCode,
            CurrencyCode = maintenance.CurrencyCode,
            ActNumber = documentNumber,
            ActDate = documentDate.ToString("dd.MM.yyyy"),
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
            ExecutorSignatoryBasis = organization.SignatoryBasis ?? string.Empty,
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
            DiscountAmount = maintenance.DiscountAmount,
            Works = maintenance.MaintenanceDetails
                .Select(detail => new MaintenanceDetail
                {
                    Name = detail.Name,
                    Quantity = detail.Quantity,
                    Description = detail.Description,
                    Price = detail.Price
                })
                .ToList()
        };
    }

    /// <summary> Создаёт модель Word-акта по данным организации, техники и обслуживания. </summary>
    public static WordMaintenanceWorkActModel CreateWord(
        OrganizationDto organization,
        GetVehicleDto vehicle,
        GetMaintenanceByIdDto maintenance,
        DateTime documentDate,
        string documentNumber)
    {
        var executorName = MaintenanceDocumentFacts.GetExecutorName(organization);

        return new WordMaintenanceWorkActModel
        {
            DocumentStyleCode = organization.DocumentStyleCode,
            ScenarioCode = organization.PrimaryScenarioCode,
            CurrencyCode = maintenance.CurrencyCode,
            ActNumber = documentNumber,
            ActDate = documentDate.ToString("dd.MM.yyyy"),
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
            ExecutorSignatoryBasis = organization.SignatoryBasis ?? string.Empty,
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
            DiscountAmount = maintenance.DiscountAmount,
            Works = maintenance.MaintenanceDetails
                .Select(detail => new WordMaintenanceDetail
                {
                    Name = detail.Name,
                    Quantity = detail.Quantity,
                    Description = detail.Description,
                    Price = (decimal?)detail.Price
                })
                .ToList()
        };
    }
}
