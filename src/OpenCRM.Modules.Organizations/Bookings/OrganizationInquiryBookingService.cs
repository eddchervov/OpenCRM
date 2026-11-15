using OpenCRM.Domain;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Bookings;

/// <summary> Координирует атомарную конвертацию обращения в плановую запись. </summary>
public sealed class OrganizationInquiryBookingService(
    IOrganizationInquiryBookingLinkService inquiryBookingLinkService,
    IOrganizationDetailingQuickBookingService detailingQuickBookingService,
    IOrganizationAutoServiceQuickBookingService autoServiceQuickBookingService,
    IOrganizationClientCommandService organizationClientCommandService,
    IOrganizationQueryService organizationQueryService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IApplicationUnitOfWork unitOfWork) : IOrganizationInquiryBookingService
{
    /// <summary> Создаёт плановую запись и фиксирует её связь с обращением. </summary>
    public async Task<OrganizationInquiryBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        CreateOrganizationInquiryBookingDto dto)
    {
        OrganizationInquiryBookingResultDto? result = null;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var detailing = dto.Detailing == null ? null : OrganizationInquiryBookingService.Copy(dto.Detailing);
            var autoService = dto.AutoService == null ? null : OrganizationInquiryBookingService.Copy(dto.AutoService);
            var newClient = dto.NewClient == null ? null : OrganizationInquiryBookingService.Copy(dto.NewClient);

            await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                userId,
                organizationId,
                OrganizationPermissionCodes.InquiriesManage);
            await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                userId,
                organizationId,
                OrganizationPermissionCodes.MaintenanceCreate);

            var inquiry = await inquiryBookingLinkService.LockAndGetAsync(userId, organizationId, inquiryId);
            if (inquiry == null)
            {
                throw new KeyNotFoundException("Обращение не найдено.");
            }

            if (inquiry.MaintenanceId.HasValue)
            {
                await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
                    userId,
                    organizationId,
                    OrganizationPermissionCodes.MaintenanceView);
                result = OrganizationInquiryBookingService.CreateResult(inquiry.MaintenanceId.Value);
                return;
            }

            if (inquiry.Version != dto.Version)
            {
                throw new InquiryConflictException("Обращение изменилось. Обновите его и повторите действие.");
            }

            if (!OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode))
            {
                throw new InquiryConflictException("Запись можно создать только из открытого обращения.");
            }

            if ((detailing == null) == (autoService == null))
            {
                throw new InvalidOperationException("Передайте данные ровно одного сценария записи.");
            }

            if (newClient != null && (detailing?.OrganizationClientId.HasValue == true
                || autoService?.OrganizationClientId.HasValue == true))
            {
                throw new InvalidOperationException("Выберите существующего клиента или создайте нового.");
            }

            if (detailing != null)
            {
                OrganizationInquiryBookingService.ValidateContactPayload(detailing.ClientName, detailing.ClientPhone);
            }

            if (autoService != null)
            {
                OrganizationInquiryBookingService.ValidateContactPayload(autoService.ClientName, autoService.ClientPhone);
            }

            var organization = await organizationQueryService.GetByIdAsync(userId, organizationId);
            var scenarioCode = string.IsNullOrWhiteSpace(organization.PrimaryScenarioCode)
                ? OrganizationTypeCodes.ServiceStation
                : organization.PrimaryScenarioCode.Trim();
            var isDetailing = string.Equals(scenarioCode, OrganizationTypeCodes.Detailing, StringComparison.OrdinalIgnoreCase);
            var isAutoService = string.Equals(scenarioCode, OrganizationTypeCodes.ServiceStation, StringComparison.OrdinalIgnoreCase);
            if ((isDetailing && detailing == null)
                || (isAutoService && autoService == null)
                || (!isDetailing && !isAutoService))
            {
                throw new InvalidOperationException("Переданные данные записи не соответствуют сценарию организации.");
            }

            Guid? clientId = null;
            if (newClient != null)
            {
                await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                    userId,
                    organizationId,
                    OrganizationPermissionCodes.ClientsCreate);
                newClient.PartyTypeCode ??= "person";
                clientId = await organizationClientCommandService.CreateForInquiryBookingAsync(userId, organizationId, newClient);
            }

            if (isDetailing)
            {
                var detailingBooking = detailing!;
                detailingBooking.OrganizationClientId = clientId ?? detailingBooking.OrganizationClientId;
                detailingBooking.IsPlannedBooking = true;
                var booking = await detailingQuickBookingService.CreateAsync(userId, organizationId, detailingBooking);
                result = OrganizationInquiryBookingService.CreateResult(booking.MaintenanceId);
            }
            else
            {
                var autoServiceBooking = autoService!;
                autoServiceBooking.OrganizationClientId = clientId ?? autoServiceBooking.OrganizationClientId;
                autoServiceBooking.IsPlannedBooking = true;
                var booking = await autoServiceQuickBookingService.CreateAsync(userId, organizationId, autoServiceBooking);
                result = OrganizationInquiryBookingService.CreateResult(booking.MaintenanceId);
            }

            await inquiryBookingLinkService.MarkBookedAsync(
                userId,
                organizationId,
                inquiryId,
                dto.Version,
                result.MaintenanceId);
        });

        return result ?? throw new InvalidOperationException("Запись не была создана.");
    }

    private static OrganizationInquiryBookingResultDto CreateResult(Guid maintenanceId) => new()
    {
        MaintenanceId = maintenanceId,
        MaintenanceUrl = $"#maintenance/{maintenanceId:D}"
    };

    private static CreateOrganizationDetailingQuickBookingDto Copy(CreateOrganizationDetailingQuickBookingDto source) => new()
    {
        VehicleId = source.VehicleId,
        RegistrationPlate = source.RegistrationPlate,
        AllowOtherRegistrationPlateFormat = source.AllowOtherRegistrationPlateFormat,
        VehicleText = source.VehicleText,
        Kind = source.Kind,
        ModelId = source.ModelId,
        ManualBrandName = source.ManualBrandName,
        ManualModelName = source.ManualModelName,
        OrganizationClientId = source.OrganizationClientId,
        ClientName = source.ClientName,
        ClientPhone = source.ClientPhone,
        ServiceId = source.ServiceId,
        Services = source.Services?.Select(OrganizationInquiryBookingService.Copy).ToList(),
        PlannedDate = source.PlannedDate,
        IsPlannedBooking = source.IsPlannedBooking,
        PlannedStartTime = source.PlannedStartTime,
        PlannedEndTime = source.PlannedEndTime,
        PlannedEndDate = source.PlannedEndDate,
        Price = source.Price,
        Location = source.Location,
        Comment = source.Comment
    };

    private static CreateOrganizationAutoServiceQuickBookingDto Copy(CreateOrganizationAutoServiceQuickBookingDto source) => new()
    {
        RegistrationPlate = source.RegistrationPlate,
        AllowOtherRegistrationPlateFormat = source.AllowOtherRegistrationPlateFormat,
        VehicleId = source.VehicleId,
        VehicleText = source.VehicleText,
        Kind = source.Kind,
        ModelId = source.ModelId,
        ManualBrandName = source.ManualBrandName,
        ManualModelName = source.ManualModelName,
        OrganizationClientId = source.OrganizationClientId,
        BuyerClientId = source.BuyerClientId,
        ClientName = source.ClientName,
        ClientPhone = source.ClientPhone,
        ServiceId = source.ServiceId,
        Services = source.Services?.Select(OrganizationInquiryBookingService.Copy).ToList(),
        PlannedDate = source.PlannedDate,
        IsPlannedBooking = source.IsPlannedBooking,
        PlannedStartTime = source.PlannedStartTime,
        PlannedEndTime = source.PlannedEndTime,
        PlannedEndDate = source.PlannedEndDate,
        Price = source.Price,
        Location = source.Location,
        Comment = source.Comment
    };

    private static CreateOrganizationClientDto Copy(CreateOrganizationClientDto source) => new()
    {
        Name = source.Name,
        LastName = source.LastName,
        FirstName = source.FirstName,
        MiddleName = source.MiddleName,
        CompanyTypeCode = source.CompanyTypeCode,
        Phone = source.Phone,
        PartyTypeCode = source.PartyTypeCode,
        Inn = source.Inn,
        Kpp = source.Kpp,
        LegalAddress = source.LegalAddress,
        Comment = source.Comment,
        StatusCode = source.StatusCode,
        ResponsibleUserId = source.ResponsibleUserId,
        Notes = source.Notes,
        TagNames = source.TagNames
    };

    private static OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDetailDto Copy(
        OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDetailDto source) => new()
    {
        DetailId = source.DetailId,
        Name = source.Name,
        Description = source.Description,
        Price = source.Price,
        VatRateCode = source.VatRateCode,
        EmployeeId = source.EmployeeId,
        EmployeeName = source.EmployeeName,
        CatalogItemId = source.CatalogItemId,
        Quantity = source.Quantity,
        MaterialCatalogItemId = source.MaterialCatalogItemId,
        MaterialName = source.MaterialName,
        MaterialQuantity = source.MaterialQuantity,
        MaterialUnit = source.MaterialUnit,
        MaterialUnitCost = source.MaterialUnitCost,
        MaterialTotalCost = source.MaterialTotalCost
    };

    private static void ValidateContactPayload(string? clientName, string? clientPhone)
    {
        if (!string.IsNullOrWhiteSpace(clientName) || !string.IsNullOrWhiteSpace(clientPhone))
        {
            throw new InvalidOperationException("Контактные данные обращения не передаются в создание клиента записи автоматически.");
        }
    }
}
