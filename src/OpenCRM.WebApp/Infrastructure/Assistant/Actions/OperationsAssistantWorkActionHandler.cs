using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

public sealed class OperationsAssistantWorkActionHandler(
    IMaintenanceQueryService maintenanceQuery,
    IVehicleOptionQueryService vehicleQuery,
    IQuickBookingVehicleLookupService vehicleLookup,
    IMaintenanceWorkQueryService workQuery,
    IMaintenanceWorkExecutionService workExecution,
    IMaintenanceWorkTimeService workTime,
    IMaintenanceWorkReportService workReports,
    IOperationMaterialQueryService operationMaterialQuery,
    IOperationMaterialCommandService operationMaterialCommand,
    IAssistantExecutionGuard executionGuard,
    AssistantOptions options) : IAssistantActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["work_start", "work_pause", "work_finish", "work_time", "work_report", "work_decision", "material_add", "material_remove"];

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new OperationsAssistantParameters(command);
        var maintenanceId = await ResolveMaintenanceIdAsync(command, actor, selection, parameters);
        var maintenance = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, maintenanceId);
        var progress = await workQuery.GetProgressAsync(actor.UserId, actor.OrganizationId, maintenanceId);
        if (progress.Count > OperationsAssistantLimits.RequirePositive(options.MaximumWorkLines, nameof(options.MaximumWorkLines)))
        {
            throw new InvalidOperationException("В заказе больше работ, чем может обработать помощник. Откройте заказ на сайте.");
        }

        if (command.Action == "work_decision")
        {
            var reports = await workQuery.GetReportsAsync(actor.UserId, actor.OrganizationId, maintenanceId);
            return PrepareDecision(command.Action, actor, parameters, selection, maintenanceId, reports, progress);
        }

        var line = SelectWorkLine(progress, parameters.OptionalText("workName"), selection);
        if (command.Action is "material_add" or "material_remove")
        {
            return await PrepareMaterialAsync(command.Action, actor, parameters, selection, maintenanceId, line, progress);
        }

        var commandId = Guid.NewGuid();
        var reason = parameters.OptionalText("reason");
        if (command.Action is "work_start" or "work_pause" or "work_finish")
        {
            if (parameters.Has("startTime") || parameters.Has("endTime") || command.StartTime is not null || command.EndTime is not null)
            {
                throw new InvalidOperationException("Начало и завершение работы фиксируются текущим временем. Для исправления используйте корректировку минут.");
            }

            var transition = command.Action switch
            {
                "work_start" when line.Status == 1 => "start",
                "work_start" when line.Status == 3 => "resume",
                "work_start" => throw new InvalidOperationException("Начать можно назначенную работу, возобновить — работу на паузе."),
                "work_pause" when line.Status == 2 => "pause",
                "work_pause" => throw new InvalidOperationException("Приостановить можно только выполняющуюся работу."),
                "work_finish" when line.Status == 2 => "complete",
                _ => throw new InvalidOperationException("Завершить можно только выполняющуюся работу.")
            };
            if (transition == "pause" && string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidOperationException("Назовите причину паузы.");
            }

            var dto = new MaintenanceWorkCommandDto { CommandId = commandId, Version = line.Version, Action = transition, Reason = reason };
            return Mutation(
                $"Заказ {maintenanceId} · {line.Name}",
                [$"{transition switch { "start" => "Начать", "resume" => "Возобновить", "pause" => "Приостановить", _ => "Завершить" }} работу «{line.Name}»"],
                new { Maintenance = maintenance, Progress = StableProgress(progress) },
                async ct =>
                {
                    await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
                    await workExecution.ExecuteAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId, dto);
                    return new AssistantActionResult($"Работа «{line.Name}» обновлена.", MaintenanceId: maintenanceId, VehicleId: maintenance.VehicleId);
                });
        }

        if (command.Action == "work_time")
        {
            var minutes = parameters.RequiredSignedInt("minutes");
            if (minutes is 0 or < -1440 or > 1440)
            {
                throw new InvalidOperationException("Корректировка времени должна быть целым числом минут от −1440 до 1440.");
            }

            var explanation = parameters.RequiredText("reason", 500);
            var dto = new AdjustMaintenanceWorkTimeDto { CommandId = commandId, Minutes = minutes, Reason = explanation };
            return Mutation(line.Name, [$"Скорректировать время на {minutes} мин.", $"Причина: {explanation}"], new { Maintenance = maintenance, Progress = StableProgress(progress) }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
                await workTime.AdjustTimeAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId, dto);
                return new AssistantActionResult($"Время работы «{line.Name}» скорректировано.", MaintenanceId: maintenanceId, VehicleId: maintenance.VehicleId);
            });
        }

        var kind = parameters.OptionalText("kind") ?? "comment";
        if (kind is not ("comment" or "defect" or "extra" or "time"))
        {
            throw new InvalidOperationException("Этот тип сообщения о работе не поддерживается.");
        }

        var text = parameters.RequiredText("text", 2000);
        var report = new CreateMaintenanceWorkReportDto { CommandId = commandId, Kind = kind, Text = text };
        return Mutation(line.Name, [$"Добавить сообщение ({kind}): {text}"], new { Maintenance = maintenance, Progress = StableProgress(progress) }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
            await workReports.AddReportAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId, report);
            return new AssistantActionResult($"Сообщение о работе «{line.Name}» добавлено.", MaintenanceId: maintenanceId, VehicleId: maintenance.VehicleId);
        });
    }

    private AssistantPreparedAction PrepareDecision(
        string action,
        AssistantActor actor,
        OperationsAssistantParameters parameters,
        AssistantActionSelection selection,
        Guid maintenanceId,
        IReadOnlyList<MaintenanceWorkReportDto> reports,
        IReadOnlyList<MaintenanceWorkProgressDto> progress)
    {
        var pendingReports = reports.Where(report => report.Decision is null).ToArray();
        MaintenanceWorkReportDto selected;
        var reportNumber = parameters.OptionalInt("reportNumber");
        if (TryGetSelection(selection, "reportNumber", out var selectedReportId))
        {
            selected = pendingReports.FirstOrDefault(report => report.Id.ToString() == selectedReportId)
                ?? throw new InvalidOperationException("Выбранное сообщение больше не найдено.");
        }
        else if (reportNumber is int number && number >= 1 && number <= pendingReports.Length)
        {
            selected = pendingReports[number - 1];
        }
        else
        {
            throw new AssistantClarificationException("Выберите сообщение, по которому нужно принять решение.", "reportNumber", OperationsAssistantLimits.Choices(options, pendingReports.Select((report, index) => new AssistantChoice(report.Id.ToString(), $"{index + 1}. {report.Text}"))));
        }
        var decision = parameters.RequiredText("decision", 20);
        var reason = parameters.OptionalText("reason");
        var price = parameters.OptionalDecimal("price");
        var vatRateCode = parameters.OptionalText("vatRateCode");
        if (decision is not ("approved" or "rejected"))
        {
            throw new InvalidOperationException("Укажите решение: согласовать или отклонить.");
        }

        if (decision == "approved" && (price is null || vatRateCode is null))
        {
            throw new InvalidOperationException("Для согласования назовите цену и ставку НДС.");
        }

        if (decision == "rejected" && reason is null)
        {
            throw new InvalidOperationException("Для отказа назовите причину.");
        }

        var dto = new DecideMaintenanceWorkReportDto { Decision = decision, Reason = reason, Price = price, VatRateCode = vatRateCode };
        return Mutation(selected.Text, [$"{(decision == "approved" ? "Согласовать" : "Отклонить")} сообщение {selected.Id}"], new { Reports = reports, Progress = StableProgress(progress) }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await workReports.DecideReportAsync(actor.UserId, actor.OrganizationId, maintenanceId, selected.Id, dto);
            return new AssistantActionResult("Решение по сообщению сохранено.", MaintenanceId: maintenanceId);
        }, destructive: true);
    }

    private async Task<AssistantPreparedAction> PrepareMaterialAsync(
        string action,
        AssistantActor actor,
        OperationsAssistantParameters parameters,
        AssistantActionSelection selection,
        Guid maintenanceId,
        MaintenanceWorkProgressDto line,
        IReadOnlyList<MaintenanceWorkProgressDto> progress)
    {
        var available = await operationMaterialQuery.GetAvailableForOperationAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId);
        if (action == "material_add")
        {
            var materialName = parameters.RequiredText("name", 150);
            var material = FindSelected(available, selection, "name", item => item.Id.ToString(), item => item.Name, materialName, "материал");
            var requestedQuantity = parameters.RequiredPositiveDecimal("quantity");
            var requestedUnit = parameters.OptionalText("unit") ?? material.Unit;
            var quantity = OperationsAssistantInventoryActionHandler.ConvertQuantityForOperation(
                requestedQuantity,
                requestedUnit,
                material.Unit,
                material.PackageQuantity);
            if (quantity > material.AvailableQuantity)
            {
                throw new InvalidOperationException($"Материала «{material.Name}» доступно только {material.AvailableQuantity} {material.Unit}.");
            }

            var dto = new IssueOperationMaterialDto
            {
                StockId = material.Id,
                Quantity = quantity,
                Unit = material.Unit,
                CommandId = Guid.NewGuid(),
                Comment = parameters.OptionalText("comment")
            };
            return Mutation(line.Name, [$"Списать {quantity} {material.Unit} «{material.Name}»"], new { Progress = StableProgress(progress), Available = available }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await operationMaterialCommand.IssueForOperationAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId, dto);
                return new AssistantActionResult($"Материал «{material.Name}» списан на работу.", MaintenanceId: maintenanceId);
            });
        }

        var issues = await operationMaterialQuery.GetIssuedForOperationAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId);
        var availableIssues = issues.Where(item => !item.IsReturn).ToArray();
        OperationMaterialIssueDto issue;
        if (TryGetSelection(selection, "issueNumber", out var selectedIssueId))
        {
            issue = availableIssues.FirstOrDefault(item => item.Id.ToString() == selectedIssueId)
                ?? throw new InvalidOperationException("Выбранная запись списания больше не найдена.");
        }
        else if (parameters.OptionalInt("issueNumber") is int issueNumber)
        {
            issue = SelectIssue(availableIssues, issueNumber);
        }
        else
        {
            issue = FindSelected(availableIssues, selection, "name", item => item.Id.ToString(), item => item.MaterialName, parameters.RequiredText("name", 150), "списанный материал");
        }
        var reason = parameters.RequiredText("reason", 500);
        var reverse = new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = reason };
        return Mutation(line.Name, [$"Вернуть {issue.Quantity} {issue.Unit} «{issue.MaterialName}»: {reason}"], new { Progress = StableProgress(progress), Issues = issues }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await operationMaterialCommand.ReverseOperationMaterialAsync(actor.UserId, actor.OrganizationId, maintenanceId, line.DetailId, issue.Id, reverse);
            return new AssistantActionResult($"Материал «{issue.MaterialName}» возвращён.", MaintenanceId: maintenanceId);
        });
    }

    private MaintenanceWorkProgressDto SelectWorkLine(IReadOnlyList<MaintenanceWorkProgressDto> items, string? name, AssistantActionSelection selection)
    {
        if (TryGetSelection(selection, "workName", out var selectedId))
        {
            return items.FirstOrDefault(item => item.DetailId.ToString() == selectedId)
                ?? throw new InvalidOperationException("Выбранная работа больше не найдена.");
        }

        if (name is not null)
        {
            return FindSelected(items, selection, "workName", item => item.DetailId.ToString(), item => item.Name, name, "работу");
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        throw new AssistantClarificationException("Уточните, с какой работой выполнить действие.", "workName", OperationsAssistantLimits.Choices(options, items.Select(item => new AssistantChoice(item.DetailId.ToString(), item.Name))));
    }

    private async Task<Guid> ResolveMaintenanceIdAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        OperationsAssistantParameters parameters)
    {
        var plate = command.RegistrationPlate ?? parameters.OptionalText("registrationPlate");
        if (string.IsNullOrWhiteSpace(plate))
        {
            if (selection.MaintenanceId is Guid selectedMaintenanceId)
            {
                return selectedMaintenanceId;
            }

            throw new AssistantClarificationException("Назовите госномер автомобиля или выберите заказ.", "registrationPlate");
        }

        var vehicleOptions = await vehicleQuery.GetOptionsAsync(actor.OrganizationId, plate, null,
            OperationsAssistantLimits.RequirePositive(options.LookupLimit, nameof(options.LookupLimit)));
        var normalizedPlate = vehicleLookup.NormalizePlateForSearch(plate);
        var vehicles = OperationsAssistantLimits.Candidates(options,
            vehicleOptions.Where(vehicle => vehicleLookup.NormalizePlateForSearch(vehicle.RegistrationPlate) == normalizedPlate));
        if (TryGetSelection(selection, "registrationPlate", out var selectedVehicleId))
        {
            vehicles = vehicles.Where(vehicle => vehicle.Id.ToString() == selectedVehicleId).ToArray();
        }

        if (vehicles.Length == 0)
        {
            throw new InvalidOperationException($"Автомобиль «{plate}» не найден.");
        }

        if (vehicles.Length > 1)
        {
            throw new AssistantClarificationException("Найдено несколько автомобилей с этим номером.", "registrationPlate", OperationsAssistantLimits.Choices(options, vehicles.Select(vehicle => new AssistantChoice(vehicle.Id.ToString(), vehicle.DisplayName))));
        }

        var orders = (await maintenanceQuery.Get(actor.UserId, actor.OrganizationId, vehicles[0].Id))
            .Where(maintenance => maintenance.Status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress)
            .OrderByDescending(maintenance => maintenance.PlannedDate ?? maintenance.Date)
            .ToArray();
        if (selection.MaintenanceId is Guid maintenanceId)
        {
            if (orders.Any(order => order.Id == maintenanceId))
            {
                return maintenanceId;
            }

            throw new InvalidOperationException("Выбранный заказ не относится к найденному автомобилю или уже закрыт.");
        }

        if (orders.Length == 1)
        {
            return orders[0].Id;
        }

        if (orders.Length == 0)
        {
            throw new InvalidOperationException("Для этого автомобиля нет запланированного или выполняющегося заказа.");
        }

        throw new AssistantClarificationException(
            "У автомобиля несколько активных заказов. Выберите заказ.",
            "maintenanceId",
            OperationsAssistantLimits.Choices(options, orders.Select(order => new AssistantChoice(order.Id.ToString(), $"{order.PlannedDate ?? order.Date:d} · {order.Status}"))));
    }

    private static bool TryGetSelection(AssistantActionSelection selection, string field, out string value)
    {
        if (selection.CatalogChoices is not null && selection.CatalogChoices.TryGetValue(field, out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }


    private T FindSelected<T>(IReadOnlyList<T> items, AssistantActionSelection selection, string field, Func<T, string> getId, Func<T, string> getLabel, string requested, string concept)
    {
        if (TryGetSelection(selection, field, out var selectedId))
        {
            return items.FirstOrDefault(item => getId(item) == selectedId)
                ?? throw new InvalidOperationException($"Выбранный {concept} больше не найден.");
        }

        var matches = OperationsAssistantLimits.Candidates(options, items.Where(item => string.Equals(getLabel(item).Trim(), requested.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"{concept} «{requested}» не найден.");
        }

        if (matches.Length > 1)
        {
            throw new AssistantClarificationException($"Найдено несколько совпадений для {concept}. Выберите нужную запись.",
                field, OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(getId(item), getLabel(item)))));
        }

        return matches[0];
    }

    private static OperationMaterialIssueDto SelectIssue(IReadOnlyList<OperationMaterialIssueDto> issues, int number)
    {
        if (number < 1 || number > issues.Count)
        {
            throw new InvalidOperationException("Списание с таким номером не найдено.");
        }

        return issues[number - 1];
    }

    private static AssistantPreparedAction Mutation(string target, IReadOnlyList<string> changes, object snapshot, Func<CancellationToken, Task<AssistantActionResult>> execute, bool destructive = false)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = changes,
            Snapshot = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)))),
            Destructive = destructive,
            ExecuteAsync = execute
        };
    }

    private static object[] StableProgress(IEnumerable<MaintenanceWorkProgressDto> progress)
    {
        return progress.Select(line => (object)new
        {
            line.DetailId,
            line.Name,
            line.EmployeeName,
            line.HasAssignedEmployee,
            line.CompletedBy,
            line.CompletionReason,
            line.Status,
            line.Version,
            line.PauseReason
        }).ToArray();
    }
}
