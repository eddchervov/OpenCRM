using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Bookings;

/// <summary> Создаёт одну запись на автомобиль рекомендации в общей транзакции. </summary>
public sealed class OrganizationRepeatVisitBookingService(
    OpenCRMContext context,
    IOrganizationRepeatVisitBookingLinkService links,
    IOrganizationDetailingQuickBookingService detailingBookings,
    IOrganizationAutoServiceQuickBookingService autoServiceBookings,
    IOrganizationSubscriptionService subscriptions,
    IApplicationUnitOfWork unitOfWork) : IOrganizationRepeatVisitBookingService
{
    public async Task<OrganizationInquiryBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        Guid repeatVisitId,
        CreateOrganizationRepeatVisitBookingDto dto)
    {
        Guid? maintenanceId = null;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);
            var recommendation = await links.LockAndGetAsync(userId, organizationId, repeatVisitId)
                ?? throw new KeyNotFoundException("Рекомендация не найдена.");
            if (recommendation.BookedMaintenanceId.HasValue)
            {
                await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
                maintenanceId = recommendation.BookedMaintenanceId.Value;
                return;
            }
            if (!recommendation.IsActive || recommendation.Version != dto.Version)
            {
                throw new RepeatVisitConflictException("Рекомендация изменена или отключена. Обновите форму записи.");
            }
            if ((dto.Detailing == null) == (dto.AutoService == null))
            {
                throw new ArgumentException("Выберите один сценарий создания записи.");
            }

            var vehicle = await context.Vehicles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == recommendation.VehicleId
                && item.OrganizationId == organizationId && item.OrganizationClientId == recommendation.ClientId && !item.IsDeleted)
                ?? throw new RepeatVisitConflictException("Автомобиль больше не относится к клиенту рекомендации.");
            var scenario = await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
                .Select(item => item.PrimaryScenarioCode).SingleAsync();
            Guid bookedVehicleId;
            if (scenario == OrganizationTypeCodes.Detailing && dto.Detailing != null)
            {
                var booking = dto.Detailing;
                if (booking.OrganizationClientId.HasValue && booking.OrganizationClientId != recommendation.ClientId)
                {
                    throw new ArgumentException("Клиента рекомендации нельзя заменить при создании записи.");
                }

                booking.OrganizationClientId = recommendation.ClientId;
                booking.RegistrationPlate = vehicle.RegistrationPlate ?? throw new ArgumentException("Для записи детейлинга нужен госномер автомобиля.");
                booking.Kind = vehicle.Kind;
                booking.ClientName = null;
                booking.ClientPhone = null;
                booking.IsPlannedBooking = true;
                var result = await detailingBookings.CreateAsync(userId, organizationId, booking);
                maintenanceId = result.MaintenanceId;
                bookedVehicleId = result.VehicleId;
            }
            else if (scenario == OrganizationTypeCodes.ServiceStation && dto.AutoService != null)
            {
                var booking = dto.AutoService;
                if (booking.OrganizationClientId.HasValue && booking.OrganizationClientId != recommendation.ClientId
                    || booking.VehicleId.HasValue && booking.VehicleId != recommendation.VehicleId)
                {
                    throw new ArgumentException("Клиента и автомобиль рекомендации нельзя заменить при создании записи.");
                }

                booking.OrganizationClientId = recommendation.ClientId;
                booking.VehicleId = recommendation.VehicleId;
                booking.RegistrationPlate = vehicle.RegistrationPlate ?? string.Empty;
                booking.ClientName = null;
                booking.ClientPhone = null;
                booking.IsPlannedBooking = true;
                var result = await autoServiceBookings.CreateAsync(userId, organizationId, booking);
                maintenanceId = result.MaintenanceId;
                bookedVehicleId = result.VehicleId;
            }
            else
            {
                throw new ArgumentException("Сценарий записи не соответствует организации.");
            }
            if (bookedVehicleId != recommendation.VehicleId)
            {
                throw new RepeatVisitConflictException("Автомобиль записи не совпадает с рекомендацией. Создание отменено.");
            }

            await links.MarkBookedAsync(userId, organizationId, repeatVisitId, maintenanceId.Value, dto.Version);
        });

        return new OrganizationInquiryBookingResultDto
        {
            MaintenanceId = maintenanceId ?? throw new InvalidOperationException("Запись не создана."),
            MaintenanceUrl = $"#maintenance/{maintenanceId:D}"
        };
    }
}
