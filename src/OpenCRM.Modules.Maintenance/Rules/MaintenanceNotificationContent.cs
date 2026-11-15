using System.Globalization;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Формирование текстов уведомлений по обслуживанию. </summary>
internal static class MaintenanceNotificationContent
{
    /// <summary> Определяет код события изменения обслуживания для уведомления. </summary>
    internal static string? GetChangeEvent(MaintenanceEntity maintenance, MaintenanceSnapshot previous)
    {
        if (previous.Status != MaintenanceStatus.Cancelled && maintenance.Status == MaintenanceStatus.Cancelled)
        {
            return "booking_cancelled";
        }

        if (!previous.ReadyForHandoverAt.HasValue && maintenance.ReadyForHandoverAt.HasValue)
        {
            return "ready";
        }

        if (maintenance.Status is (MaintenanceStatus.Planned or MaintenanceStatus.InProgress)
            && (previous.PlannedDate != maintenance.PlannedDate
                || previous.PlannedStartTime != maintenance.PlannedStartTime
                || previous.PlannedEndDate != maintenance.PlannedEndDate
                || previous.PlannedEndTime != maintenance.PlannedEndTime))
        {
            return "booking_changed";
        }

        return null;
    }

    /// <summary> Формирует текст письма уведомления по обслуживанию. </summary>
    internal static string BuildBody(
        MaintenanceEntity maintenance,
        string organizationName,
        string? scenarioCode,
        string? registrationPlate,
        string eventCode)
    {
        var date = (maintenance.PlannedDate ?? maintenance.Date).ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        var time = maintenance.PlannedStartTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var when = time == null ? date : $"{date} в {time}";
        var isFleet = scenarioCode == OrganizationTypeCodes.Fleet;
        var vehicleType = isFleet ? "технику" : "автомобиль";
        var vehicleName = string.IsNullOrWhiteSpace(registrationPlate) ? vehicleType : $"{vehicleType} {registrationPlate}";
        var readyVehicleName = isFleet
            ? string.IsNullOrWhiteSpace(registrationPlate) ? "техника" : $"техника {registrationPlate}"
            : vehicleName;
        return eventCode switch
        {
            "booking_created" => $"{organizationName}: запись на {vehicleName} оформлена на {when}.",
            "booking_changed" => $"{organizationName}: время записи на {vehicleName} изменено на {when}.",
            "booking_cancelled" => $"{organizationName}: запись на {vehicleName} отменена.",
            "ready" => $"{organizationName}: {readyVehicleName} {(isFleet ? "готова" : "готов")} к выдаче.",
            _ => throw new InvalidOperationException("Неизвестное событие уведомления.")
        };
    }
}
