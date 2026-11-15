using System.Text.Json;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.AiPilot;

/// <summary> Применяет общую политику номеров к созданию и изменению техники помощником. </summary>
public static class AiPilotRegistrationPlateGuard
{
    /// <summary> Возвращает уточнение для неподтверждённого нестандартного номера или типа техники. </summary>
    public static AssistantClarification? GetClarification(AiPilotCommand command, IVehicleRegistrationPlatePolicy policy)
    {
        if (command.Action is not ("visit" or "book" or "vehicle_create" or "vehicle_update"))
        {
            return null;
        }

        var isPlateChange = command.Action == "vehicle_update";
        if (isPlateChange && !command.Parameters.ContainsKey("vehicleKind"))
        {
            return null;
        }

        var plate = isPlateChange
            ? command.Parameters.GetValueOrDefault("newRegistrationPlate") is { ValueKind: JsonValueKind.String } replacement ? replacement.GetString() : null
            : command.RegistrationPlate;
        if (string.IsNullOrWhiteSpace(plate))
        {
            return null;
        }

        var kind = VehicleKind.PassengerCar;
        if (command.Parameters.TryGetValue("vehicleKind", out var kindValue) && kindValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            if (kindValue.ValueKind != JsonValueKind.Number || !kindValue.TryGetInt32(out var parsedKind) || !Enum.IsDefined((VehicleKind)parsedKind))
            {
                return new AssistantClarification("parameter:vehicleKind", "Уточните тип техники: легковой автомобиль, грузовой, прицеп, мотоцикл или другая техника.", []);
            }

            kind = (VehicleKind)parsedKind;
        }

        var field = isPlateChange ? "parameter:newRegistrationPlate" : "registrationPlate.confirm";
        try
        {
            var confirmed = !string.IsNullOrWhiteSpace(command.ConfirmedRegistrationPlate) &&
                string.Equals(policy.Normalize(command.ConfirmedRegistrationPlate), policy.Normalize(plate), StringComparison.Ordinal);
            if (confirmed || policy.MatchesFormat(plate, kind))
            {
                policy.Validate(plate, kind, allowOtherFormat: confirmed);

                return null;
            }
        }
        catch (InvalidOperationException exception)
        {
            return new AssistantClarification(field, exception.Message, []);
        }

        return new AssistantClarification(field,
            $"Проверьте госномер «{plate}»: он не подходит к маске выбранного типа техники. Повторите номер, называя буквы отдельно, или уточните тип техники. Если это правильный номер другого формата, скажите «номер верный».", []);
    }
}
