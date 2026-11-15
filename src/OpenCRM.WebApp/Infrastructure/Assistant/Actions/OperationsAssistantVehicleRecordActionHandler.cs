using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Defects;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

public sealed class OperationsAssistantVehicleRecordActionHandler(
    IVehicleOptionQueryService vehicleQuery,
    IQuickBookingVehicleLookupService vehicleLookup,
    IVehicleCounterQueryService counterQuery,
    IVehicleCounterCommandService counterCommand,
    IVehicleDefectService defectService,
    IAssistantExecutionGuard executionGuard,
    AssistantOptions options) : IAssistantActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["defect_list", "defect_create", "defect_update", "defect_close", "defect_delete", "counter_add", "counter_remove"];

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new OperationsAssistantParameters(command);
        var vehicle = await ResolveVehicleAsync(command, actor, selection);
        if (command.Action.StartsWith("defect_", StringComparison.Ordinal))
        {
            return await PrepareDefectAsync(command.Action, actor, parameters, selection, vehicle.Id);
        }

        return await PrepareCounterAsync(command.Action, actor, parameters, selection, vehicle);
    }

    private async Task<AssistantPreparedAction> PrepareDefectAsync(
        string action,
        AssistantActor actor,
        OperationsAssistantParameters parameters,
        AssistantActionSelection selection,
        Guid vehicleId)
    {
        var page = await defectService.GetAsync(actor.UserId, actor.OrganizationId, page: 1,
            pageSize: OperationsAssistantLimits.PageSize(options), vehicleId: vehicleId);
        var defects = page.Items;
        if (action == "defect_list")
        {
            return ReadOnly("Неисправности автомобиля", defects.Select(defect => $"{defect.Title}: {defect.StatusName}").ToArray(), page);
        }

        if (page.HasMore)
        {
            throw new InvalidOperationException("Список неисправностей большой. Найдите нужную запись в карточке автомобиля.");
        }

        if (action == "defect_create")
        {
            var title = parameters.RequiredText("name", 200);
            var severity = parameters.OptionalText("severityCode") ?? "medium";
            var status = parameters.OptionalText("statusCode") ?? "new";
            if (!IsSeverity(severity) || !IsStatus(status))
            {
                throw new InvalidOperationException("Проверьте важность и статус неисправности.");
            }

            var dto = new CreateVehicleDefectDto
            {
                VehicleId = vehicleId,
                Title = title,
                Description = parameters.OptionalText("description"),
                SeverityCode = severity,
                StatusCode = status
            };
            return Mutation("Автомобиль", [$"Добавить неисправность «{title}»"], page, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await defectService.CreateAsync(actor.UserId, actor.OrganizationId, dto);
                return new AssistantActionResult($"Неисправность «{title}» добавлена.", VehicleId: vehicleId);
            });
        }

        var defectName = parameters.RequiredText("name", 200);
        var defect = FindUnique(defects, defectName, selection);
        if (action == "defect_delete")
        {
            if (defect.LinkedMaintenanceCount > 0)
            {
                throw new InvalidOperationException("У неисправности есть связанные обслуживания. Удалить её нельзя.");
            }

            return Mutation(defect.Title, [$"Удалить неисправность «{defect.Title}»"], new { Page = page, Defect = defect }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await defectService.DeleteAsync(actor.UserId, actor.OrganizationId, defect.Id);
                return new AssistantActionResult($"Неисправность «{defect.Title}» удалена.", VehicleId: vehicleId);
            }, destructive: true);
        }

        if (action == "defect_close")
        {
            if (defect.IsClosed)
            {
                throw new InvalidOperationException("Эта неисправность уже закрыта.");
            }

            return Mutation(defect.Title, [$"Закрыть неисправность «{defect.Title}»"], new { Page = page, Defect = defect }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await defectService.UpdateStatusAsync(actor.UserId, actor.OrganizationId, defect.Id, "resolved");
                return new AssistantActionResult($"Неисправность «{defect.Title}» закрыта.", VehicleId: vehicleId);
            });
        }

        var updated = new UpdateVehicleDefectDto
        {
            VehicleId = vehicleId,
            Title = parameters.OptionalText("newName") ?? defect.Title,
            Description = parameters.Has("description") ? parameters.OptionalText("description") : defect.Description,
            SeverityCode = parameters.OptionalText("severityCode") ?? defect.SeverityCode,
            StatusCode = parameters.OptionalText("statusCode") ?? defect.StatusCode
        };
        if (!IsSeverity(updated.SeverityCode) || !IsStatus(updated.StatusCode))
        {
            throw new InvalidOperationException("Проверьте важность и статус неисправности.");
        }

        return Mutation(defect.Title, [$"Изменить неисправность «{defect.Title}»"], new { Page = page, Defect = defect }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await defectService.UpdateAsync(actor.UserId, actor.OrganizationId, defect.Id, updated);
            return new AssistantActionResult($"Неисправность «{defect.Title}» обновлена.", VehicleId: vehicleId);
        });
    }

    private async Task<AssistantPreparedAction> PrepareCounterAsync(
        string action,
        AssistantActor actor,
        OperationsAssistantParameters parameters,
        AssistantActionSelection selection,
        VehicleOptionDto vehicle)
    {
        var entries = await counterQuery.GetCounterHistoryAsync(actor.UserId, actor.OrganizationId, vehicle.Id,
            OperationsAssistantLimits.RequirePositive(options.MaintenanceHistoryLimit, nameof(options.MaintenanceHistoryLimit)));
        if (action == "counter_add")
        {
            var value = parameters.RequiredInt("value");
            var latest = entries.OrderByDescending(entry => entry.RecordedAt).FirstOrDefault();
            if (latest is not null && value < latest.Value)
            {
                throw new InvalidOperationException($"Новое показание меньше последнего ({latest.Value}). Уточните историю автомобиля.");
            }

            var recordedAt = parameters.OptionalDateTime("recordedAt")
                ?? ComposeRecordedAt(parameters.OptionalDateFromCommand(), commandStartTime: parameters.OptionalText("startTime"));
            var request = new UpdateVehicleCounterDto
            {
                VehicleId = vehicle.Id,
                Value = value,
                RecordedAt = recordedAt,
                Comment = parameters.OptionalText("comment")
            };
            return Mutation(vehicle.DisplayName, [$"Записать показание {value}"], entries, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await counterCommand.UpdateCounterAsync(actor.UserId, actor.OrganizationId, request);
                return new AssistantActionResult($"Показание {value} записано.", VehicleId: vehicle.Id);
            });
        }

        var orderedEntries = entries.OrderByDescending(value => value.RecordedAt).ToArray();
        VehicleCounterEntryDto entry;
        if (TryGetSelection(selection, "entryNumber", out var selectedEntryId))
        {
            entry = orderedEntries.FirstOrDefault(value => value.Id.ToString() == selectedEntryId)
                ?? throw new InvalidOperationException("Выбранная запись показания больше не найдена.");
        }
        else
        {
            var number = parameters.OptionalInt("entryNumber");
            if (number is null)
            {
                throw new AssistantClarificationException("Уточните, какую запись показания удалить.", "entryNumber", OperationsAssistantLimits.Choices(options, orderedEntries.Select((value, index) => new AssistantChoice(value.Id.ToString(), $"{index + 1}. {value.Value} от {value.RecordedAt:g}"))));
            }

            if (number > entries.Count || number < 1)
            {
                throw new InvalidOperationException("Показание с таким номером не найдено.");
            }

            entry = orderedEntries[number.Value - 1];
        }
        if (!entry.CanManage)
        {
            throw new InvalidOperationException("У пользователя нет права удалять это показание.");
        }

        return Mutation($"{vehicle.DisplayName}: {entry.Value}", ["Удалить запись показания"], new { Entries = entries, Entry = entry }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await counterCommand.DeleteCounterEntryAsync(actor.UserId, actor.OrganizationId, entry.Id);
            return new AssistantActionResult("Запись показания удалена.", VehicleId: vehicle.Id);
        }, destructive: true);
    }

    private async Task<VehicleOptionDto> ResolveVehicleAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection)
    {
        var plate = command.RegistrationPlate?.Trim();
        if (string.IsNullOrWhiteSpace(plate) || plate.Length > 32)
        {
            throw new AssistantClarificationException("Назовите госномер автомобиля.", "registrationPlate");
        }

        var vehicles = await vehicleQuery.GetOptionsAsync(actor.OrganizationId, plate, null,
            OperationsAssistantLimits.RequirePositive(options.LookupLimit, nameof(options.LookupLimit)));
        var normalizedPlate = vehicleLookup.NormalizePlateForSearch(plate);
        var matches = OperationsAssistantLimits.Candidates(options,
            vehicles.Where(vehicle => vehicleLookup.NormalizePlateForSearch(vehicle.RegistrationPlate) == normalizedPlate));
        if (TryGetSelection(selection, "registrationPlate", out var selectedVehicleId))
        {
            return matches.FirstOrDefault(vehicle => vehicle.Id.ToString() == selectedVehicleId)
                ?? throw new InvalidOperationException("Выбранный автомобиль больше не найден.");
        }

        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Автомобиль с номером «{plate}» не найден.");
        }

        if (matches.Length > 1)
        {
            throw new AssistantClarificationException("Найдено несколько автомобилей с этим номером. Выберите нужный автомобиль.", "registrationPlate", OperationsAssistantLimits.Choices(options, matches.Select(vehicle => new AssistantChoice(vehicle.Id.ToString(), vehicle.DisplayName))));
        }

        return matches[0];
    }

    private VehicleDefectDto FindUnique(IEnumerable<VehicleDefectDto> defects, string name, AssistantActionSelection selection)
    {
        var matches = defects.Where(defect => string.Equals(defect.Title.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (TryGetSelection(selection, "name", out var selectedDefectId))
        {
            return matches.FirstOrDefault(defect => defect.Id.ToString() == selectedDefectId)
                ?? throw new InvalidOperationException("Выбранная неисправность больше не найдена.");
        }

        matches = OperationsAssistantLimits.Candidates(options, matches);
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Неисправность «{name}» не найдена.");
        }

        if (matches.Length > 1)
        {
            throw new AssistantClarificationException("Найдено несколько одинаковых неисправностей. Выберите запись.", "name", OperationsAssistantLimits.Choices(options, matches.Select(defect => new AssistantChoice(defect.Id.ToString(), defect.Title))));
        }

        return matches[0];
    }

    private static bool IsSeverity(string value) => value is "low" or "medium" or "high" or "critical";

    private static bool IsStatus(string value) => value is "new" or "in_progress" or "resolved";

    private static bool TryGetSelection(AssistantActionSelection selection, string field, out string value)
    {
        if (selection.CatalogChoices is not null && selection.CatalogChoices.TryGetValue(field, out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static DateTime ComposeRecordedAt(DateOnly? date, string? commandStartTime)
    {
        if (date is null)
        {
            return DateTime.UtcNow;
        }

        var time = TimeOnly.TryParseExact(commandStartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
            ? parsedTime
            : new TimeOnly(12, 0);
        return date.Value.ToDateTime(time, DateTimeKind.Unspecified);
    }

    private static AssistantPreparedAction ReadOnly(string target, IReadOnlyList<string> changes, object snapshot)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = changes,
            Snapshot = Fingerprint(snapshot),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(snapshot),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult("Данные показаны."))
        };
    }

    private static AssistantPreparedAction Mutation(string target, IReadOnlyList<string> changes, object snapshot, Func<CancellationToken, Task<AssistantActionResult>> execute, bool destructive = false)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = changes,
            Snapshot = Fingerprint(snapshot),
            Destructive = destructive,
            ExecuteAsync = execute
        };
    }

    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
