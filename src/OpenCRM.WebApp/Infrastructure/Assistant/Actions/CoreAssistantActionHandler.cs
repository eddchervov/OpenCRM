using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

/// <summary>Выполняет разрешённые команды по обслуживанию, автомобилям и клиентам организации.</summary>
public sealed class CoreAssistantActionHandler(
    IMaintenanceQueryService maintenanceQuery,
    IMaintenanceCreateService maintenanceCreate,
    IMaintenanceUpdateService maintenanceUpdate,
    IMaintenanceDeleteService maintenanceDelete,
    IMaintenanceWorkflowService maintenanceWorkflow,
    IMaintenanceSchedulingService maintenanceScheduling,
    IMaintenancePaymentService maintenancePayment,
    IMaintenanceAssignmentService maintenanceAssignment,
    IMaintenanceIntakeService maintenanceIntake,
    IVehicleQueryService vehicleQuery,
    IVehicleCommandService vehicleCommand,
    IOrganizationClientQueryService clientQuery,
    IOrganizationClientCommandService clientCommand,
    IOrganizationEmployeeService employeeService,
    ICatalogQueryService catalogQuery,
    IAssistantExecutionGuard guard,
    AssistantOptions options,
    IVehicleRegistrationPlatePolicy? registrationPlates = null) : IAssistantActionHandler
{
    private static readonly Regex s_workPostNumberPattern = new(
        @"^(?:ПОСТ|БОКС|ПОДЪ[ЕЁ]МНИК)?(\d+)(?:Й|ОЙ|ЫЙ)?(?:ПОСТ|БОКС|ПОДЪ[ЕЁ]МНИК)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] s_actionNames =
    [
        "visit", "book", "arrive", "start", "complete", "reschedule", "cancel", "reopen",
        "return_to_planned", "return_to_work", "mark_ready", "handover", "undo_handover", "simple_handover",
        "maintenance_get", "maintenance_update", "maintenance_delete", "service_add", "service_update",
        "service_remove", "services_replace", "payment_add", "payment_cancel", "assign_employee",
        "assign_location", "intake_update", "vehicle_get", "vehicle_create", "vehicle_update",
        "vehicle_archive", "vehicle_restore", "client_get", "client_create", "client_update", "client_comment"
    ];

    public IReadOnlyCollection<string> Actions => s_actionNames;

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        if (!s_actionNames.Contains(command.Action, StringComparer.Ordinal))
        {
            throw new AssistantClarificationException("Эта команда пока не поддерживается.", "action");
        }

        return command.Action switch
        {
            "vehicle_get" => await PrepareVehicleGetAsync(command, actor, selection, cancellationToken),
            "vehicle_create" => await PrepareVehicleCreateAsync(command, actor, selection, cancellationToken),
            "vehicle_update" or "vehicle_archive" or "vehicle_restore" => await PrepareVehicleChangeAsync(command, actor, selection, cancellationToken),
            "client_get" => await PrepareClientGetAsync(command, actor, selection, cancellationToken),
            "client_create" => await PrepareClientCreateAsync(command, actor, selection, cancellationToken),
            "client_update" or "client_comment" => await PrepareClientChangeAsync(command, actor, selection, cancellationToken),
            "book" or "visit" => await PrepareMaintenanceCreateAsync(command, actor, selection, cancellationToken),
            "arrive" => await PrepareArriveAsync(command, actor, selection, cancellationToken),
            "maintenance_get" => await PrepareMaintenanceGetAsync(command, actor, selection, cancellationToken),
            "maintenance_update" => await PrepareMaintenanceUpdateAsync(command, actor, selection, cancellationToken),
            "maintenance_delete" => await PrepareMaintenanceDeleteAsync(command, actor, selection, cancellationToken),
            "service_add" or "service_update" or "service_remove" or "services_replace" => await PrepareServiceChangeAsync(command, actor, selection, cancellationToken),
            "payment_add" or "payment_cancel" => await PreparePaymentChangeAsync(command, actor, selection, cancellationToken),
            "assign_employee" => await PrepareAssignEmployeeAsync(command, actor, selection, cancellationToken),
            "assign_location" => await PrepareAssignLocationAsync(command, actor, selection, cancellationToken),
            "intake_update" => await PrepareIntakeUpdateAsync(command, actor, selection, cancellationToken),
            _ => await PrepareWorkflowAsync(command, actor, selection, cancellationToken)
        };
    }

    private async Task<AssistantPreparedAction> PrepareMaintenanceCreateAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        GetVehicleDto? vehicle = null;
        CreateVehicleDto? newVehicle = null;
        if (GetGuid(command, "vehicleId", selection).HasValue)
        {
            vehicle = await ResolveVehicleAsync(command, actor, selection, ct);
        }
        else
        {
            var plate = Require(command.RegistrationPlate, "registrationPlate");
            var found = await FindVehiclesAsync(actor, plate, ct);
            if (found.Length > 1)
            {
                throw new AssistantClarificationException("Нашлось несколько автомобилей. Выберите нужный.", "vehicleId", OperationsAssistantLimits.Choices(options, found.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.DisplayName} {item.RegistrationPlate}"))));
            }

            if (found.Length == 1)
            {
                vehicle = found[0];
            }
            else
            {
                newVehicle = new CreateVehicleDto
                {
                    RegistrationPlate = plate,
                    Kind = (VehicleKind)(GetInt(command, "vehicleKind") ?? (int)VehicleKind.PassengerCar),
                    AllowOtherRegistrationPlateFormat = string.Equals(command.ConfirmedRegistrationPlate, plate, StringComparison.Ordinal),
                    Name = plate,
                    OrganizationId = actor.OrganizationId
                };
                await guard.EnsureAllowedAsync(actor, "vehicle_create", ct);
            }
        }

        var details = await this.ResolveServicesAsync(command.Services, actor, selection, ct, useCatalogPrices: command.Action == "book");
        if (details.Count == 0)
        {
            throw new AssistantClarificationException("Какая работа была выполнена и сколько она стоила?", "services");
        }

        if (command.Action == "visit" && command.TotalAmount.HasValue && details.Count == 1 && details[0].Price is null)
        {
            details[0].Price = command.TotalAmount.Value;
        }

        if (command.Action == "book")
        {
            if (command.TotalAmount.HasValue && details.Count == 1 && command.Services[0].Price is null)
            {
                details[0].Price = command.TotalAmount.Value;
            }

            for (var index = 0; index < details.Count; index++)
            {
                if (details[index].Price.HasValue || command.Services[index].IsPriceDeferred)
                {
                    continue;
                }

                throw new AssistantClarificationException(
                    $"Сколько будет стоить работа «{details[index].Name}»? Если цена ещё не определена, ответьте «пока неизвестно».",
                    $"book.price:{index}");
            }
        }

        if (command.Action == "visit" && details.Any(item => item.Price is null))
        {
            throw new AssistantClarificationException(details.Count > 1
                ? "Назовите стоимость каждой выполненной работы отдельно."
                : "Укажите стоимость выполненной работы.", "price");
        }

        if (command.PaidAmount < 0)
        {
            throw new AssistantClarificationException("Сумма оплаты не может быть отрицательной.", "paidAmount");
        }

        var paidAmount = command.PaidAmount is > 0 ? command.PaidAmount : null;
        if (paidAmount.HasValue)
        {
            if (details.Any(item => !item.Price.HasValue) || paidAmount > details.Sum(item => item.Price.GetValueOrDefault() * item.Quantity))
            {
                throw new AssistantClarificationException("Оплата не должна превышать стоимость работ. Уточните цены и полученную сумму.", "paidAmount");
            }

            await guard.EnsureAllowedAsync(actor, "payment_add", ct);
        }

        var plannedDate = command.Action == "book" ? ParseDate(command.Date, "date") : null;
        var startTime = ParseTime(command.StartTime, "startTime");
        var endTime = ParseTime(command.EndTime, "endTime");
        var workPost = command.Action == "book" ? await this.ResolveBookingWorkPostAsync(command, actor, selection, ct) : null;
        var workPostSnapshot = Fingerprint(workPost?.Id, workPost?.Name);
        var snapshot = vehicle is null
            ? Fingerprint(newVehicle!.RegistrationPlate, newVehicle.Name, newVehicle.Kind, details, plannedDate, startTime, endTime, workPostSnapshot)
            : Fingerprint(vehicle.Id, vehicle.RegistrationPlate, vehicle.OrganizationClientId, details, plannedDate, startTime, endTime, workPostSnapshot);
        var plateValue = vehicle?.RegistrationPlate ?? newVehicle!.RegistrationPlate;
        var changes = new List<string> { command.Action switch { "book" => "Создать запись", "arrive" => "Начать обслуживание", _ => "Зафиксировать выполненное обслуживание" } };
        if (newVehicle is not null)
        {
            changes.Add($"Создать автомобиль {newVehicle.RegistrationPlate}");
        }

        if (plannedDate.HasValue)
        {
            changes.Add($"Дата: {plannedDate.Value:dd.MM.yyyy}");
        }

        if (command.Action == "book" && startTime.HasValue)
        {
            changes.Add($"Начало: {startTime.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}");
        }

        if (command.Action == "book" && endTime.HasValue)
        {
            changes.Add($"Окончание: {endTime.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}");
        }

        if (workPost is not null)
        {
            changes.Add($"Пост: {workPost.Name}");
        }

        changes.AddRange(details.Select(item => $"{item.Name}: {item.Price?.ToString("0.##", CultureInfo.InvariantCulture) ?? "цена уточняется"} ₽"));
        if (command.Action == "book" && command.TotalAmount.HasValue && details.Count > 1 && details.All(item => item.Price.HasValue))
        {
            var workTotal = details.Sum(item => item.Price!.Value * item.Quantity);
            if (workTotal != command.TotalAmount.Value)
            {
                changes.Add($"Проверьте сумму: названо {command.TotalAmount.Value.ToString("0.##", CultureInfo.InvariantCulture)} ₽, сумма работ {workTotal.ToString("0.##", CultureInfo.InvariantCulture)} ₽.");
            }
        }

        if (paidAmount.HasValue)
        {
            changes.Add($"Записать оплату {paidAmount.Value.ToString("0.##", CultureInfo.InvariantCulture)} ₽");
        }

        return new AssistantPreparedAction
        {
            Target = $"Автомобиль {plateValue}",
            Changes = changes,
            Snapshot = Fingerprint(snapshot, paidAmount),
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                if (paidAmount.HasValue)
                {
                    await guard.EnsureAllowedAsync(actor, "payment_add", token);
                }

                if (workPost is not null)
                {
                    var freshPost = await this.ResolveBookingWorkPostAsync(command, actor, selection, token);
                    EnsureSnapshot(workPostSnapshot, Fingerprint(freshPost?.Id, freshPost?.Name));
                }

                if (command.Action == "book")
                {
                    var freshDetails = await this.ResolveServicesAsync(command.Services, actor, selection, token, useCatalogPrices: true);
                    if (command.TotalAmount.HasValue && freshDetails.Count == 1 && command.Services[0].Price is null)
                    {
                        freshDetails[0].Price = command.TotalAmount.Value;
                    }

                    EnsureSnapshot(Fingerprint(details), Fingerprint(freshDetails));
                }

                Guid targetVehicleId;
                if (newVehicle is not null)
                {
                    await guard.EnsureAllowedAsync(actor, "vehicle_create", token);
                    var currentMatches = await FindVehiclesAsync(actor, newVehicle.RegistrationPlate!, token);
                    if (currentMatches.Length != 0)
                    {
                        throw new AssistantClarificationException("Автомобиль с таким номером уже появился. Подготовьте действие заново.", "vehicleId", OperationsAssistantLimits.Choices(options, currentMatches.Select(item => new AssistantChoice(item.Id.ToString(), item.DisplayName))));
                    }

                    targetVehicleId = await vehicleCommand.Create(actor.UserId, actor.OrganizationId, newVehicle);
                }
                else
                {
                    var currentVehicle = await vehicleQuery.GetById(actor.UserId, actor.OrganizationId, vehicle!.Id);
                    EnsureSnapshot(Fingerprint(snapshot, paidAmount), Fingerprint(
                        Fingerprint(currentVehicle.Id, currentVehicle.RegistrationPlate, currentVehicle.OrganizationClientId, details, plannedDate, startTime, endTime, workPostSnapshot),
                        paidAmount));
                    targetVehicleId = vehicle.Id;
                }

                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var maintenanceId = await maintenanceCreate.Create(actor.UserId, actor.OrganizationId, targetVehicleId, new CreateMaintenanceDto
                {
                    Status = command.Action switch { "book" => MaintenanceStatus.Planned, "arrive" => MaintenanceStatus.InProgress, _ => MaintenanceStatus.Completed },
                    Type = MaintenanceType.Repair,
                    PlannedDate = plannedDate,
                    PlannedStartTime = startTime,
                    PlannedEndTime = endTime,
                    CompletedDate = command.Action == "visit" ? DateTime.UtcNow : null,
                    Location = workPost?.Name,
                    Performer = null,
                    Mileage = null,
                    Comment = command.Comment,
                    PaidAmount = 0,
                    Details = details
                });
                if (paidAmount.HasValue)
                {
                    await guard.EnsureAllowedAsync(actor, "payment_add", token);
                    await maintenancePayment.AddPayment(actor.UserId, actor.OrganizationId, maintenanceId, new AddMaintenancePaymentDto
                    {
                        Id = Guid.NewGuid(),
                        Amount = paidAmount.Value,
                        MethodCode = command.PaymentMethod,
                        ExpectedPaidAmount = 0
                    });
                }
                return new AssistantActionResult(command.Action == "arrive" ? "Обслуживание начато." : "Запись создана.", maintenanceId, targetVehicleId);
            }
        };
    }

    private async Task<OrganizationScenarioCatalogItemDto?> ResolveBookingWorkPostAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = selection.CatalogChoices?.GetValueOrDefault("locationName") ?? GetString(command, "locationName");
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var catalog = await catalogQuery.GetAsync(actor.UserId, actor.OrganizationId);
        var activePosts = catalog.WorkPosts.Where(item => item.IsActive).ToArray();
        var selectedId = Guid.TryParse(requested, out var parsedId) ? parsedId : (Guid?)null;
        var matches = OperationsAssistantLimits.Candidates(options, activePosts.Where(item => selectedId.HasValue
            ? item.Id == selectedId.Value
            : Normalize(item.Name) == Normalize(requested)));
        var number = GetWorkPostNumber(requested);
        if (!selectedId.HasValue && matches.Length == 0 && number.HasValue)
        {
            matches = OperationsAssistantLimits.Candidates(options, activePosts.Where(item => GetWorkPostNumber(item.Name) == number));
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        var choices = matches.Length > 0 ? matches : activePosts;
        throw new AssistantClarificationException(
            matches.Length == 0 ? "Такого активного поста нет. Выберите пост для записи." : "Под этим номером найдено несколько постов. Выберите нужный.",
            "locationName", OperationsAssistantLimits.Choices(options, choices.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
    }

    private static long? GetWorkPostNumber(string value)
    {
        var match = s_workPostNumberPattern.Match(Normalize(value));

        return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : null;
    }

    private async Task<AssistantPreparedAction> PrepareArriveAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        GetVehicleDto vehicle;
        var selectedMaintenanceId = selection.MaintenanceId ?? GetGuid(command, "maintenanceId");
        var selectedVehicleId = GetGuid(command, "vehicleId", selection);
        if (selectedVehicleId.HasValue)
        {
            vehicle = await ResolveVehicleAsync(command, actor, selection, ct);
        }
        else if (selectedMaintenanceId.HasValue)
        {
            var selectedMaintenance = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, selectedMaintenanceId.Value);
            vehicle = await vehicleQuery.GetById(actor.UserId, actor.OrganizationId, selectedMaintenance.VehicleId);
            if (!string.IsNullOrWhiteSpace(command.RegistrationPlate)
                && !string.Equals(Normalize(vehicle.RegistrationPlate), Normalize(command.RegistrationPlate), StringComparison.OrdinalIgnoreCase))
            {
                throw new AssistantClarificationException("Выбранная запись относится к другому автомобилю.", "maintenanceId");
            }
        }
        else
        {
            var plate = Require(command.RegistrationPlate, "registrationPlate");
            var vehicles = await FindVehiclesAsync(actor, plate, ct);
            if (vehicles.Length > 1)
            {
                throw new AssistantClarificationException("Нашлось несколько автомобилей. Выберите нужный.", "vehicleId", OperationsAssistantLimits.Choices(options, vehicles.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.DisplayName} {item.RegistrationPlate}"))));
            }

            if (vehicles.Length == 0)
            {
                return await PrepareMaintenanceCreateAsync(command, actor, selection, ct);
            }

            vehicle = vehicles[0];
        }

        var active = (await maintenanceQuery.Get(actor.UserId, actor.OrganizationId, vehicle.Id))
            .Where(item => item.Status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress)
            .ToArray();
        if (selectedMaintenanceId.HasValue)
        {
            active = active.Where(item => item.Id == selectedMaintenanceId.Value).ToArray();
            if (active.Length == 0)
            {
                throw new AssistantClarificationException("Выбранная запись уже не запланирована и не выполняется.", "maintenanceId");
            }
        }

        if (active.Length > OperationsAssistantLimits.RequirePositive(options.MaintenanceHistoryLimit, nameof(options.MaintenanceHistoryLimit)))
        {
            throw new InvalidOperationException("Слишком много активных записей. Выберите запись явно на сайте.");
        }

        if (active.Length == 0)
        {
            return await PrepareMaintenanceCreateAsync(command, actor, selection, ct);
        }

        if (!selectedMaintenanceId.HasValue && active.Length > 1)
        {
            throw new AssistantClarificationException("У автомобиля несколько активных записей. Выберите нужную.", "maintenanceId", OperationsAssistantLimits.Choices(options, active.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.PlannedDate ?? item.Date:d} — {item.Status}"))));
        }

        var maintenance = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, active[0].Id);
        var changes = new List<string>();
        if (command.Services.Count > 0 || command.TotalAmount.HasValue || command.PaidAmount.HasValue)
        {
            changes.Add("Используются работы и цены существующей записи. Продиктованные новые работы, суммы и оплата этой командой не вносятся; для них нужна отдельная команда.");
        }

        if (maintenance.Status == MaintenanceStatus.InProgress)
        {
            return new AssistantPreparedAction
            {
                Target = GetMaintenanceLabel(maintenance),
                Changes = ["Обслуживание уже выполняется", .. changes],
                Snapshot = Fingerprint(maintenance),
                ReadOnly = true,
                Data = JsonSerializer.SerializeToElement(maintenance),
                ExecuteAsync = _ => Task.FromResult(new AssistantActionResult("Это обслуживание уже выполняется.", maintenance.Id, maintenance.VehicleId))
            };
        }

        await guard.EnsureAllowedAsync(actor, "start", ct);
        var workflow = new MaintenanceWorkflowActionDto { Action = "start", Version = maintenance.Version };
        var snapshot = Fingerprint(maintenance);
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(maintenance),
            Changes = ["Начать запланированное обслуживание", .. changes],
            Snapshot = snapshot,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, "start", token);
                var fresh = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, maintenance.Id);
                EnsureSnapshot(snapshot, Fingerprint(fresh));
                await maintenanceWorkflow.ChangeWorkflow(actor.UserId, actor.OrganizationId, maintenance.Id, workflow);
                return new AssistantActionResult("Обслуживание начато.", maintenance.Id, maintenance.VehicleId);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareMaintenanceGetAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var maintenance = await ResolveMaintenanceAsync(command, actor, selection, ct);
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(maintenance),
            Changes = ["Показать данные записи"],
            Snapshot = Fingerprint(maintenance),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(maintenance),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult(GetMaintenanceLabel(maintenance), maintenance.Id, maintenance.VehicleId))
        };
    }

    private async Task<AssistantPreparedAction> PrepareMaintenanceUpdateAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var updated = BuildUpdatedMaintenance(command, current);
        return MaintenanceMutation(command, actor, current, "Изменить заказ", async token =>
        {
            await maintenanceUpdate.Update(actor.UserId, actor.OrganizationId, current.Id, updated);
            return new AssistantActionResult("Данные записи обновлены.", current.Id, current.VehicleId);
        });
    }

    private async Task<AssistantPreparedAction> PrepareMaintenanceDeleteAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(current),
            Changes = ["Удалить запись обслуживания"],
            Snapshot = Fingerprint(current),
            Destructive = true,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, current.Id);
                EnsureSnapshot(Fingerprint(current), Fingerprint(fresh));
                await maintenanceDelete.Delete(actor.UserId, actor.OrganizationId, current.Id);
                return new AssistantActionResult("Запись удалена.", current.Id, current.VehicleId);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareServiceChangeAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var rows = current.MaintenanceDetails.ToList();
        var details = rows.Select(ToCreateDetail).ToList();
        var line = command.Action is "service_update" or "service_remove" ? ResolveLine(command, rows, selection) : null;
        switch (command.Action)
        {
            case "service_add":
                var newService = (await ResolveServicesAsync([new AiPilotServiceLine { Name = GetString(command, "serviceName") ?? string.Empty, Price = GetDecimal(command, "price") }], actor, selection, ct))[0];
                newService.Quantity = GetInt(command, "quantity") ?? 1;
                newService.Description = GetString(command, "description");
                newService.VatRateCode = GetString(command, "vatRateCode");
                var employee = await ResolveEmployeeAsync(actor, GetString(command, "employeeName", selection), GetGuid(command, "employeeId", selection), ct);
                newService.EmployeeId = employee?.Id;
                newService.EmployeeName = employee?.Name;
                details.Add(newService);
                break;
            case "service_update":
                var index = rows.IndexOf(line!);
                details[index] = ToCreateDetail(line!);
                details[index].Name = GetString(command, "newName") ?? details[index].Name;
                details[index].Price = GetDecimal(command, "price") ?? details[index].Price;
                details[index].Quantity = GetInt(command, "quantity") ?? details[index].Quantity;
                details[index].Description = GetString(command, "description") ?? details[index].Description;
                details[index].VatRateCode = GetString(command, "vatRateCode") ?? details[index].VatRateCode;
                if (command.Parameters.ContainsKey("employeeName") || command.Parameters.ContainsKey("employeeId") || selection.CatalogChoices?.ContainsKey("employeeId") == true)
                {
                    var updatedEmployee = await ResolveEmployeeAsync(actor, GetString(command, "employeeName", selection), GetGuid(command, "employeeId", selection), ct);
                    details[index].EmployeeId = updatedEmployee?.Id;
                    details[index].EmployeeName = updatedEmployee?.Name;
                }
                if (!string.IsNullOrWhiteSpace(GetString(command, "newName")))
                {
                    var replacement = (await ResolveServicesAsync(
                        [new AiPilotServiceLine { Name = GetString(command, "newName")!, Price = details[index].Price }],
                        actor,
                        selection,
                        ct))[0];
                    replacement.DetailId = details[index].DetailId;
                    replacement.Quantity = details[index].Quantity;
                    replacement.Description = details[index].Description;
                    replacement.VatRateCode = details[index].VatRateCode;
                    replacement.EmployeeId = details[index].EmployeeId;
                    replacement.EmployeeName = details[index].EmployeeName;
                    details[index] = replacement;
                }
                break;
            case "service_remove":
                details.RemoveAt(rows.IndexOf(line!));
                break;
            case "services_replace":
                if (command.Services.Count == 0)
                {
                    throw new AssistantClarificationException("Назовите работы, которые должны остаться в заказе.", "services");
                }
                details = await ResolveServicesAsync(command.Services, actor, selection, ct);
                break;
        }

        if (details.Count == 0)
        {
            throw new AssistantClarificationException("В заказе должна остаться хотя бы одна работа. Какую работу добавить?", "services");
        }

        var snapshot = Fingerprint(current);
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(current),
            Changes = [command.Action == "services_replace" ? "Заменить список работ" : "Изменить список работ", .. details.Select(item => $"{item.Name}: {item.Price?.ToString("0.##", CultureInfo.InvariantCulture) ?? "цена не указана"} ₽")],
            Snapshot = snapshot,
            Destructive = command.Action is "service_remove" or "services_replace",
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, current.Id);
                EnsureSnapshot(snapshot, Fingerprint(fresh));
                await maintenanceUpdate.UpdateServices(actor.UserId, actor.OrganizationId, current.Id, new UpdateMaintenanceServicesDto { Version = current.Version ?? 0, Details = details });
                return new AssistantActionResult("Работы в заказе обновлены.", current.Id, current.VehicleId);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareWorkflowAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var workflow = new MaintenanceWorkflowActionDto
        {
            Action = command.Action,
            Version = current.Version,
            PlannedDate = ParseOptionalDate(command.Date),
            PlannedStartTime = ParseTime(command.StartTime, "startTime"),
            PlannedEndTime = ParseTime(command.EndTime, "endTime"),
            PlannedEndDate = ParseOptionalDate(GetString(command, "plannedEndDate")),
            CompletedDate = command.Action == "complete" ? DateTime.UtcNow : null,
            Reason = GetString(command, "reason")
        };
        if ((command.Action is "reschedule" or "reopen") && !workflow.PlannedDate.HasValue)
        {
            throw new AssistantClarificationException(command.Action == "reopen" ? "На какую дату вернуть запись?" : "На какую дату перенести запись?", "date");
        }
        var prepared = MaintenanceMutation(command, actor, current, $"Выполнить действие «{command.Action}»", async token =>
        {
            if (command.Action == "reschedule")
            {
                await maintenanceScheduling.UpdatePlannedDate(actor.UserId, actor.OrganizationId, current.Id, new UpdateMaintenancePlannedDateDto
                {
                    PlannedDate = workflow.PlannedDate!.Value,
                    PlannedEndDate = workflow.PlannedEndDate,
                    PlannedStartTime = workflow.PlannedStartTime,
                    PlannedEndTime = workflow.PlannedEndTime
                });
                return new AssistantActionResult("Запись перенесена.", current.Id, current.VehicleId);
            }
            await maintenanceWorkflow.ChangeWorkflow(actor.UserId, actor.OrganizationId, current.Id, workflow);
            return new AssistantActionResult("Статус записи обновлён.", current.Id, current.VehicleId);
        });
        if (command.Action == "cancel")
        {
            return new AssistantPreparedAction
            {
                Target = prepared.Target,
                Changes = prepared.Changes,
                Snapshot = prepared.Snapshot,
                Destructive = true,
                ExecuteAsync = prepared.ExecuteAsync
            };
        }

        return prepared;
    }

    private async Task<AssistantPreparedAction> PreparePaymentChangeAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var payments = await maintenancePayment.GetPayments(actor.UserId, actor.OrganizationId, current.Id);
        if (command.Action == "payment_add")
        {
            var amount = command.PaidAmount ?? GetDecimal(command, "amount");
            if (amount is null or <= 0)
            {
                throw new AssistantClarificationException("Какую сумму оплаты записать?", "amount");
            }
            return MaintenanceMutation(command, actor, current, $"Принять оплату {amount:0.##} ₽", async token =>
            {
                await maintenancePayment.AddPayment(actor.UserId, actor.OrganizationId, current.Id, new AddMaintenancePaymentDto
                {
                    Id = Guid.NewGuid(),
                    Amount = amount.Value,
                    MethodCode = command.PaymentMethod,
                    ExpectedPaidAmount = current.PaidAmount
                });
                return new AssistantActionResult("Оплата добавлена.", current.Id, current.VehicleId);
            });
        }

        var number = GetInt(command, "paymentNumber", selection);
        var amountToCancel = GetDecimal(command, "amount");
        var matching = payments.Where(item => item.CancelledAt is null && (!amountToCancel.HasValue || item.Amount == amountToCancel.Value)).ToList();
        if (number.HasValue)
        {
            matching = number.Value > 0 && number.Value <= matching.Count ? [matching[number.Value - 1]] : [];
        }
        if (matching.Count != 1)
        {
            throw new AssistantClarificationException("Уточните, какую оплату отменить.", "paymentNumber", OperationsAssistantLimits.Choices(options, matching.Select((payment, index) => new AssistantChoice((index + 1).ToString(CultureInfo.InvariantCulture), $"{payment.Amount:0.##} ₽"))));
        }

        var reason = GetString(command, "reason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AssistantClarificationException("Почему нужно отменить оплату?", "reason");
        }
        var selected = matching[0];
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(current),
            Changes = [$"Отменить оплату {selected.Amount:0.##} ₽", $"Причина: {reason}"],
            Snapshot = Fingerprint(current, payments),
            Destructive = true,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, current.Id);
                EnsureSnapshot(Fingerprint(current, payments), Fingerprint(fresh, await maintenancePayment.GetPayments(actor.UserId, actor.OrganizationId, current.Id)));
                await maintenancePayment.CancelPayment(actor.UserId, actor.OrganizationId, current.Id, selected.Id, reason);
                return new AssistantActionResult("Оплата отменена.", current.Id, current.VehicleId);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareAssignEmployeeAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var employeeName = GetString(command, "employeeName", selection);
        var employee = await ResolveEmployeeAsync(actor, employeeName, GetGuid(command, "employeeId", selection), ct);
        var employeeId = employee?.Id;
        return MaintenanceMutation(command, actor, current, employeeId.HasValue ? "Назначить сотрудника" : "Снять назначение сотрудника", async token =>
        {
            await maintenanceAssignment.AssignEmployee(actor.UserId, actor.OrganizationId, current.Id, new AssignMaintenanceEmployeeDto { EmployeeId = employeeId, Version = current.Version ?? 0 });
            return new AssistantActionResult("Исполнитель обновлён.", current.Id, current.VehicleId);
        });
    }

    private async Task<AssistantPreparedAction> PrepareAssignLocationAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var location = GetString(command, "locationName", selection);
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new AssistantClarificationException("Назовите пост или место выполнения работ.", "locationName");
        }
        var catalog = await catalogQuery.GetAsync(actor.UserId, actor.OrganizationId);
        var selectedPostId = Guid.TryParse(location, out var parsedLocationId) ? parsedLocationId : (Guid?)null;
        var posts = OperationsAssistantLimits.Candidates(options, catalog.WorkPosts.Where(item => item.IsActive && (selectedPostId.HasValue
            ? item.Id == selectedPostId.Value
            : string.Equals(Normalize(item.Name), Normalize(location), StringComparison.OrdinalIgnoreCase))));
        if (posts.Length != 1)
        {
            throw new AssistantClarificationException(posts.Length == 0 ? "Такого активного поста нет в справочнике организации." : "Уточните, какой пост назначить.", "locationName", OperationsAssistantLimits.Choices(options, posts.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }
        location = posts[0].Name;
        var updated = BuildUpdatedMaintenance(command, current);
        updated.Location = location;
        return MaintenanceMutation(command, actor, current, $"Назначить пост «{location}»", async token =>
        {
            await maintenanceUpdate.Update(actor.UserId, actor.OrganizationId, current.Id, updated);
            return new AssistantActionResult("Пост назначен.", current.Id, current.VehicleId);
        });
    }

    private async Task<AssistantPreparedAction> PrepareIntakeUpdateAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveMaintenanceAsync(command, actor, selection, ct);
        var dto = new SaveMaintenanceIntakeDto
        {
            Mileage = command.Parameters.ContainsKey("mileage") ? GetInt(command, "mileage") : current.IntakeMileage,
            Completeness = command.Parameters.ContainsKey("completeness") ? GetString(command, "completeness") : current.IntakeCompleteness,
            VisibleDamage = command.Parameters.ContainsKey("visibleDamage") ? GetString(command, "visibleDamage") : current.IntakeVisibleDamage,
            Notes = command.Parameters.ContainsKey("notes") ? GetString(command, "notes") : current.IntakeNotes
        };
        return MaintenanceMutation(command, actor, current, "Обновить данные приёмки", async token =>
        {
            await maintenanceIntake.SaveIntake(actor.UserId, actor.OrganizationId, current.Id, dto);
            return new AssistantActionResult("Данные приёмки сохранены.", current.Id, current.VehicleId);
        });
    }

    private async Task<AssistantPreparedAction> PrepareVehicleGetAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var vehicle = await ResolveVehicleAsync(command, actor, selection, ct);
        return new AssistantPreparedAction
        {
            Target = $"Автомобиль {vehicle.RegistrationPlate}",
            Changes = ["Показать данные автомобиля"],
            Snapshot = Fingerprint(vehicle),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(vehicle),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult($"{vehicle.DisplayName} {vehicle.RegistrationPlate}", VehicleId: vehicle.Id))
        };
    }

    private async Task<AssistantPreparedAction> PrepareVehicleCreateAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var plate = Require(command.RegistrationPlate, "registrationPlate");
        var existing = await FindVehiclesAsync(actor, plate, ct);
        if (existing.Length > 0)
        {
            var selectedId = GetGuid(command, "vehicleId", selection);
            if (selectedId.HasValue)
            {
                var selected = existing.SingleOrDefault(item => item.Id == selectedId.Value)
                    ?? throw new AssistantClarificationException("Выбранный автомобиль не совпадает с госномером.", "vehicleId");
                return new AssistantPreparedAction
                {
                    Target = $"Автомобиль {selected.RegistrationPlate}",
                    Changes = ["Использовать существующий автомобиль"],
                    Snapshot = Fingerprint(selected),
                    ReadOnly = true,
                    Data = JsonSerializer.SerializeToElement(selected),
                    ExecuteAsync = _ => Task.FromResult(new AssistantActionResult("Использую существующий автомобиль.", VehicleId: selected.Id))
                };
            }
            throw new AssistantClarificationException("Автомобиль с таким номером уже существует. Выберите его или уточните госномер.", "vehicleId", OperationsAssistantLimits.Choices(options, existing.Select(item => new AssistantChoice(item.Id.ToString(), item.DisplayName))));
        }

        var name = GetString(command, "name");
        var dto = new CreateVehicleDto
        {
            RegistrationPlate = plate,
            Kind = (VehicleKind)(GetInt(command, "vehicleKind") ?? (int)VehicleKind.PassengerCar),
            AllowOtherRegistrationPlateFormat = string.Equals(command.ConfirmedRegistrationPlate, plate, StringComparison.Ordinal),
            Name = string.IsNullOrWhiteSpace(name) ? plate : name,
            Vin = GetString(command, "vin"),
            YearIssue = GetInt(command, "yearIssue"),
            Color = GetString(command, "color"),
            ManualBrandName = GetString(command, "manualBrandName"),
            ManualModelName = GetString(command, "manualModelName"),
            Description = GetString(command, "description"),
            BodyNumber = GetString(command, "bodyNumber"),
            EngineNumber = GetString(command, "engineNumber"),
            RegistrationCertificate = GetString(command, "registrationCertificate"),
            CommissioningDate = ParseOptionalDate(GetString(command, "commissioningDate")),
            OrganizationId = actor.OrganizationId
        };
        await guard.EnsureAllowedAsync(actor, command.Action, ct);
        var snap = Fingerprint(actor.OrganizationId, plate, dto.Name, dto.Vin, dto.YearIssue, dto.Color, dto.ManualBrandName, dto.ManualModelName);
        return new AssistantPreparedAction
        {
            Target = $"Автомобиль {plate}",
            Changes = ["Создать автомобиль", string.IsNullOrWhiteSpace(dto.ManualBrandName) || string.IsNullOrWhiteSpace(dto.ManualModelName)
                ? $"Госномер: {plate}"
                : $"Марка и модель: {dto.ManualBrandName} {dto.ManualModelName}"],
            Snapshot = snap,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var currentMatches = await FindVehiclesAsync(actor, plate, token);
                if (currentMatches.Length != 0)
                {
                    throw new AssistantClarificationException("Автомобиль с таким номером уже существует. Подготовьте действие заново.", "vehicleId", OperationsAssistantLimits.Choices(options, currentMatches.Select(item => new AssistantChoice(item.Id.ToString(), item.DisplayName))));
                }
                var id = await vehicleCommand.Create(actor.UserId, actor.OrganizationId, dto);
                return new AssistantActionResult("Автомобиль создан.", VehicleId: id);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareVehicleChangeAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var vehicle = await ResolveVehicleAsync(command, actor, selection, ct, command.Action == "vehicle_restore");
        if (command.Action == "vehicle_update" && registrationPlates is not null)
        {
            command.Parameters.TryAdd("vehicleKind", JsonSerializer.SerializeToElement((int)vehicle.Kind));
            var issue = AiPilotRegistrationPlateGuard.GetClarification(command, registrationPlates);
            if (issue is not null)
            {
                throw new AssistantClarificationException(issue.Message, issue.Field, issue.Choices);
            }
        }

        var snapshot = Fingerprint(vehicle);
        return new AssistantPreparedAction
        {
            Target = $"Автомобиль {vehicle.RegistrationPlate}",
            Changes = [command.Action switch { "vehicle_archive" => "Поместить в архив", "vehicle_restore" => "Восстановить из архива", _ => "Изменить данные автомобиля" }],
            Snapshot = snapshot,
            Destructive = command.Action == "vehicle_archive",
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await vehicleQuery.GetById(actor.UserId, actor.OrganizationId, vehicle.Id);
                EnsureSnapshot(snapshot, Fingerprint(fresh));
                if (command.Action == "vehicle_archive")
                {
                    await vehicleCommand.Archive(actor.UserId, actor.OrganizationId, vehicle.Id);
                }
                else if (command.Action == "vehicle_restore")
                {
                    await vehicleCommand.RestoreFromArchive(actor.UserId, actor.OrganizationId, vehicle.Id);
                }
                else
                {
                    await vehicleCommand.Update(actor.UserId, actor.OrganizationId, vehicle.Id, new UpdateVehicleDto
                    {
                        Kind = (VehicleKind)(GetInt(command, "vehicleKind") ?? (int)vehicle.Kind),
                        MileageUnit = vehicle.MileageUnit,
                        ModelId = vehicle.ModelId,
                        YearIssue = GetInt(command, "yearIssue") ?? vehicle.YearIssue,
                        ManualBrandName = GetString(command, "manualBrandName") ?? vehicle.ManualBrandName,
                        ManualModelName = GetString(command, "manualModelName") ?? vehicle.ManualModelName,
                        Name = GetString(command, "name") ?? vehicle.Name,
                        Vin = GetString(command, "vin") ?? vehicle.Vin,
                        RegistrationPlate = GetString(command, "newRegistrationPlate") ?? vehicle.RegistrationPlate,
                        AllowOtherRegistrationPlateFormat = string.Equals(command.ConfirmedRegistrationPlate, GetString(command, "newRegistrationPlate"), StringComparison.Ordinal) && command.ConfirmedRegistrationPlate is not null,
                        BodyNumber = GetString(command, "bodyNumber") ?? vehicle.BodyNumber,
                        EngineNumber = GetString(command, "engineNumber") ?? vehicle.EngineNumber,
                        Color = GetString(command, "color") ?? vehicle.Color,
                        RegistrationCertificate = GetString(command, "registrationCertificate") ?? vehicle.RegistrationCertificate,
                        CommissioningDate = ParseOptionalDate(GetString(command, "commissioningDate")) ?? vehicle.CommissioningDate,
                        Description = GetString(command, "description") ?? vehicle.Description,
                        OrganizationClientId = vehicle.OrganizationClientId
                    });
                }
                return new AssistantActionResult("Данные автомобиля обновлены.", VehicleId: vehicle.Id);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareClientGetAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var client = await ResolveClientAsync(command, actor, selection, ct);
        return new AssistantPreparedAction
        {
            Target = client.Name,
            Changes = ["Показать данные клиента"],
            Snapshot = Fingerprint(client),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(client),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult(client.Name, ClientId: client.Id))
        };
    }

    private async Task<AssistantPreparedAction> PrepareClientCreateAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var name = GetString(command, "clientName") ?? GetString(command, "newName") ?? GetString(command, "name");
        var phone = GetString(command, "phone") ?? GetString(command, "newPhone");
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
        {
            throw new AssistantClarificationException("Назовите имя клиента или его телефон.", "clientName");
        }
        var dto = new CreateOrganizationClientDto
        {
            Name = name ?? phone!,
            FirstName = GetString(command, "firstName"),
            LastName = GetString(command, "lastName"),
            MiddleName = GetString(command, "middleName"),
            Phone = phone,
            PartyTypeCode = GetString(command, "partyTypeCode"),
            CompanyTypeCode = GetString(command, "companyTypeCode"),
            Inn = GetString(command, "inn"),
            Kpp = GetString(command, "kpp"),
            LegalAddress = GetString(command, "legalAddress"),
            Comment = GetString(command, "comment"),
            StatusCode = GetString(command, "statusCode"),
            TagNames = (GetString(command, "tagNamesText") ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        };
        var existing = OperationsAssistantLimits.Candidates(options, await clientQuery.GetAsync(actor.UserId, actor.OrganizationId, phone ?? name));
        if (existing.Length > 0)
        {
            var selectedId = GetGuid(command, "clientId", selection);
            if (selectedId.HasValue)
            {
                var selected = existing.SingleOrDefault(item => item.Id == selectedId.Value)
                    ?? throw new AssistantClarificationException("Выбранный клиент не совпадает с поиском.", "clientId");
                return new AssistantPreparedAction
                {
                    Target = selected.Name,
                    Changes = ["Использовать существующего клиента"],
                    Snapshot = Fingerprint(selected),
                    ReadOnly = true,
                    Data = JsonSerializer.SerializeToElement(selected),
                    ExecuteAsync = _ => Task.FromResult(new AssistantActionResult("Использую существующего клиента.", ClientId: selected.Id))
                };
            }
            throw new AssistantClarificationException("Похожий клиент уже есть. Выберите карточку или уточните данные.", "clientId", OperationsAssistantLimits.Choices(options, existing.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }

        var snapshot = Fingerprint(actor.OrganizationId, dto.Name, dto.Phone);
        return new AssistantPreparedAction
        {
            Target = dto.Name,
            Changes = ["Создать карточку клиента"],
            Snapshot = snapshot,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var matches = OperationsAssistantLimits.Candidates(options, await clientQuery.GetAsync(actor.UserId, actor.OrganizationId, phone ?? name));
                if (matches.Length != 0)
                {
                    throw new AssistantClarificationException("Похожий клиент уже есть. Выберите карточку или уточните данные.", "clientId", OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
                }
                var id = await clientCommand.CreateAsync(actor.UserId, actor.OrganizationId, dto);
                return new AssistantActionResult("Клиент создан.", ClientId: id);
            }
        };
    }

    private async Task<AssistantPreparedAction> PrepareClientChangeAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var current = await ResolveClientAsync(command, actor, selection, ct);
        var updated = new UpdateOrganizationClientDto
        {
            Name = GetString(command, "newName") ?? current.Name,
            FirstName = GetString(command, "firstName") ?? current.FirstName,
            LastName = GetString(command, "lastName") ?? current.LastName,
            MiddleName = GetString(command, "middleName") ?? current.MiddleName,
            Phone = GetString(command, "newPhone") ?? current.Phone,
            PartyTypeCode = GetString(command, "partyTypeCode") ?? current.PartyTypeCode,
            CompanyTypeCode = GetString(command, "companyTypeCode") ?? current.CompanyTypeCode,
            Inn = GetString(command, "inn") ?? current.Inn,
            Kpp = GetString(command, "kpp") ?? current.Kpp,
            LegalAddress = GetString(command, "legalAddress") ?? current.LegalAddress,
            Comment = current.Comment,
            StatusCode = current.StatusCode,
            ResponsibleUserId = current.ResponsibleUserId,
            TagNames = GetString(command, "tagNamesText") is { } tagNames
                ? tagNames.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : current.Tags.Select(tag => tag.Name).ToList()
        };
        var snapshot = Fingerprint(current);
        return new AssistantPreparedAction
        {
            Target = current.Name,
            Changes = [command.Action == "client_comment" ? "Изменить примечание клиента" : "Изменить карточку клиента"],
            Snapshot = snapshot,
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await clientQuery.GetByIdAsync(actor.UserId, actor.OrganizationId, current.Id)
                    ?? throw new AssistantClarificationException("Клиент больше не найден.", "clientId");
                EnsureSnapshot(snapshot, Fingerprint(fresh));
                if (command.Action == "client_comment")
                {
                    var comment = GetString(command, "comment");
                    if (comment is null)
                    {
                        throw new AssistantClarificationException("Какой комментарий добавить или убрать у клиента?", "comment");
                    }
                    await clientCommand.UpdateCommentAsync(actor.UserId, actor.OrganizationId, current.Id, new UpdateOrganizationClientCommentDto { Comment = comment });
                }
                else
                {
                    updated.Comment = GetString(command, "comment") ?? current.Comment;
                    await clientCommand.UpdateAsync(actor.UserId, actor.OrganizationId, current.Id, updated);
                }
                return new AssistantActionResult("Карточка клиента обновлена.", ClientId: current.Id);
            }
        };
    }

    private async Task<GetVehicleDto> ResolveVehicleAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct, bool isArchived = false)
    {
        var id = GetGuid(command, "vehicleId", selection);
        if (id.HasValue)
        {
            var vehicle = await vehicleQuery.GetById(actor.UserId, actor.OrganizationId, id.Value);
            if (vehicle.OrganizationId != actor.OrganizationId)
            {
                throw new AssistantClarificationException("Автомобиль не относится к этой организации.", "vehicleId");
            }
            if (!string.IsNullOrWhiteSpace(command.RegistrationPlate)
                && !string.Equals(Normalize(vehicle.RegistrationPlate), Normalize(command.RegistrationPlate), StringComparison.OrdinalIgnoreCase))
            {
                throw new AssistantClarificationException("Выбранный автомобиль не совпадает с госномером.", "vehicleId");
            }
            return vehicle;
        }

        var plate = Require(command.RegistrationPlate, "registrationPlate");
        var found = await FindVehiclesAsync(actor, plate, ct, isArchived);
        if (found.Length == 0)
        {
            throw new AssistantClarificationException($"Автомобиль {plate} не найден. Уточните номер или создайте автомобиль отдельно.", "registrationPlate");
        }
        if (found.Length > 1)
        {
            throw new AssistantClarificationException("Нашлось несколько автомобилей. Выберите нужный.", "vehicleId", OperationsAssistantLimits.Choices(options, found.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.DisplayName} {item.RegistrationPlate}"))));
        }
        return found[0];
    }

    private async Task<GetVehicleDto[]> FindVehiclesAsync(AssistantActor actor, string plate, CancellationToken ct, bool isArchived = false)
    {
        var response = await vehicleQuery.Get(actor.UserId, actor.OrganizationId, 1, OperationsAssistantLimits.PageSize(options), isArchived, searchQuery: plate, canViewMaintenance: true);
        return OperationsAssistantLimits.Candidates(options,
            response.Items.Where(item => string.Equals(Normalize(item.RegistrationPlate), Normalize(plate), StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<GetMaintenanceByIdDto> ResolveMaintenanceAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var id = selection.MaintenanceId ?? GetGuid(command, "maintenanceId");
        if (id.HasValue)
        {
            var selected = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, id.Value);
            if (!string.IsNullOrWhiteSpace(command.RegistrationPlate))
            {
                var selectedVehicle = await vehicleQuery.GetById(actor.UserId, actor.OrganizationId, selected.VehicleId);
                if (!string.Equals(Normalize(selectedVehicle.RegistrationPlate), Normalize(command.RegistrationPlate), StringComparison.OrdinalIgnoreCase))
                {
                    throw new AssistantClarificationException("Выбранная запись относится к другому автомобилю.", "maintenanceId");
                }
            }
            return selected;
        }

        var vehicle = await ResolveVehicleAsync(command, actor, selection, ct);
        var records = (await maintenanceQuery.Get(actor.UserId, actor.OrganizationId, vehicle.Id)).ToList();
        var date = ParseOptionalDate(GetString(command, "recordDate"));
        if (date.HasValue)
        {
            records = records.Where(item => item.Date.Date == date.Value.Date || item.PlannedDate?.Date == date.Value.Date).ToList();
        }
        if (IsTrue(command, "latest"))
        {
            records = records.OrderByDescending(item => item.Date).Take(1).ToList();
        }
        if (records.Count > OperationsAssistantLimits.RequirePositive(options.MaintenanceHistoryLimit, nameof(options.MaintenanceHistoryLimit)))
        {
            throw new InvalidOperationException("История заказов большая. Уточните дату или откройте карточку автомобиля.");
        }
        if (records.Count == 0)
        {
            throw new AssistantClarificationException("Для этого автомобиля запись не найдена.", "maintenanceId");
        }
        if (records.Count > 1)
        {
            throw new AssistantClarificationException("У автомобиля несколько записей. Уточните дату или выберите запись.", "maintenanceId", OperationsAssistantLimits.Choices(options, records.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.Date:dd.MM.yyyy} — {item.Status}"))));
        }
        return await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, records[0].Id);
    }

    private async Task<OrganizationClientDto> ResolveClientAsync(AiPilotCommand command, AssistantActor actor, AssistantActionSelection selection, CancellationToken ct)
    {
        var id = GetGuid(command, "clientId", selection);
        if (id.HasValue)
        {
            return await clientQuery.GetByIdAsync(actor.UserId, actor.OrganizationId, id.Value)
                ?? throw new AssistantClarificationException("Клиент не найден.", "clientId");
        }
        var search = GetString(command, "clientName", selection) ?? GetString(command, "phone", selection);
        if (string.IsNullOrWhiteSpace(search))
        {
            throw new AssistantClarificationException("Кого найти? Назовите имя клиента или телефон.", "clientName");
        }
        var clients = OperationsAssistantLimits.Candidates(options, await clientQuery.GetAsync(actor.UserId, actor.OrganizationId, search));
        if (clients.Length != 1)
        {
            throw new AssistantClarificationException(clients.Length == 0 ? "Клиент не найден." : "Нашлось несколько клиентов. Выберите нужного.", "clientId", OperationsAssistantLimits.Choices(options, clients.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }
        return clients[0];
    }

    private async Task<OrganizationEmployeeDto?> ResolveEmployeeAsync(AssistantActor actor, string? name, Guid? employeeId, CancellationToken ct)
    {
        if (!employeeId.HasValue && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var employees = await employeeService.GetAsync(actor.UserId, actor.OrganizationId);
        var matches = OperationsAssistantLimits.Candidates(options, employees.Where(item => item.IsActive && (employeeId.HasValue
            ? item.Id == employeeId.Value
            : string.Equals(Normalize(item.Name), Normalize(name), StringComparison.OrdinalIgnoreCase))));
        if (matches.Length != 1)
        {
            throw new AssistantClarificationException(matches.Length == 0 ? "Сотрудник не найден в организации." : "Уточните, какого сотрудника назначить.", "employeeId", OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }

        return matches[0];
    }

    private async Task<List<CreateMaintenanceDetailDto>> ResolveServicesAsync(
        IReadOnlyList<AiPilotServiceLine> services,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken ct,
        bool useCatalogPrices = false)
    {
        var catalog = await catalogQuery.GetAsync(actor.UserId, actor.OrganizationId);
        if (services.Count > OperationsAssistantLimits.RequirePositive(options.MaximumWorkLines, nameof(options.MaximumWorkLines)))
        {
            throw new InvalidOperationException("В команде больше работ, чем может обработать помощник.");
        }

        var active = catalog.Services.Where(item => item.IsActive);
        var result = new List<CreateMaintenanceDetailDto>();
        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Name))
            {
                throw new AssistantClarificationException("Как называется работа?", "serviceName");
            }
            var matches = OperationsAssistantLimits.Candidates(options,
                active.Where(item => string.Equals(Normalize(item.Name), Normalize(service.Name), StringComparison.OrdinalIgnoreCase)));
            OrganizationScenarioCatalogItemDto? catalogItem = null;
            var name = service.Name.Trim();
            if (matches.Length == 1)
            {
                catalogItem = matches[0];
                name = matches[0].Name;
            }
            else if (matches.Length > 1)
            {
                var key = $"catalog:{result.Count}:{Normalize(service.Name)}";
                if (selection.CatalogChoices?.TryGetValue(key, out var chosen) != true || !Guid.TryParse(chosen, out var chosenId) || matches.All(item => item.Id != chosenId))
                {
                    throw new AssistantClarificationException($"В справочнике есть несколько похожих работ «{service.Name}». Выберите одну.", key, OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
                }
                catalogItem = matches.Single(item => item.Id == chosenId);
                name = catalogItem.Name;
            }

            result.Add(new CreateMaintenanceDetailDto
            {
                Name = name,
                Price = service.Price ?? (useCatalogPrices && !service.IsPriceDeferred ? catalogItem?.UnitPrice : null),
                Quantity = 1,
                CatalogItemId = catalogItem?.Id
            });
        }
        return result;
    }

    private AssistantPreparedAction MaintenanceMutation(AiPilotCommand command, AssistantActor actor, GetMaintenanceByIdDto current, string description, Func<CancellationToken, Task<AssistantActionResult>> mutate)
    {
        return new AssistantPreparedAction
        {
            Target = GetMaintenanceLabel(current),
            Changes = [description],
            Snapshot = Fingerprint(current),
            ExecuteAsync = async token =>
            {
                await guard.EnsureAllowedAsync(actor, command.Action, token);
                var fresh = await maintenanceQuery.GetById(actor.UserId, actor.OrganizationId, current.Id);
                EnsureSnapshot(Fingerprint(current), Fingerprint(fresh));
                return await mutate(token);
            }
        };
    }

    private static UpdateMaintenanceDto BuildUpdatedMaintenance(AiPilotCommand command, GetMaintenanceByIdDto current) => new()
    {
        BuyerClientId = current.BuyerClientId,
        ChangeBuyer = false,
        Version = current.Version,
        Status = current.Status,
        Type = current.Type,
        PlannedDate = ParseOptionalDate(command.Date) ?? current.PlannedDate,
        PlannedStartTime = ParseTime(command.StartTime, "startTime") ?? current.PlannedStartTime,
        PlannedEndTime = ParseTime(command.EndTime, "endTime") ?? current.PlannedEndTime,
        PlannedEndDate = ParseOptionalDate(GetString(command, "plannedEndDate")) ?? current.PlannedEndDate,
        CompletedDate = ParseOptionalDate(GetString(command, "completedDate")) ?? current.CompletedDate,
        Location = current.Location,
        EmployeeId = current.EmployeeId,
        Performer = GetString(command, "performer") ?? current.Performer,
        Mileage = GetInt(command, "mileage") ?? current.Mileage,
        Comment = GetString(command, "comment") ?? current.Comment,
        DiscountAmount = GetDecimal(command, "discountAmount") ?? current.DiscountAmount,
        PaidAmount = current.PaidAmount,
        LaborCostAmount = GetDecimal(command, "laborCostAmount") ?? current.LaborCostAmount,
        Details = current.MaintenanceDetails.Select(ToCreateDetail).ToList()
    };

    private static CreateMaintenanceDetailDto ToCreateDetail(MaintenanceDetailDto item) => new()
    {
        DetailId = item.DetailId,
        Name = item.Name,
        Description = item.Description,
        Price = item.Price,
        VatRateCode = item.VatRateCode,
        EmployeeId = item.EmployeeId,
        EmployeeName = item.EmployeeName,
        CatalogItemId = item.CatalogItemId,
        Quantity = item.Quantity
    };

    private MaintenanceDetailDto? ResolveLine(AiPilotCommand command, IReadOnlyList<MaintenanceDetailDto> rows, AssistantActionSelection selection)
    {
        var name = GetString(command, "serviceName", selection);
        var matches = rows.Where(item => string.IsNullOrWhiteSpace(name) || string.Equals(Normalize(item.Name), Normalize(name), StringComparison.OrdinalIgnoreCase)).ToList();
        var number = GetInt(command, "lineNumber", selection);
        if (number.HasValue)
        {
            if (number.Value > 0 && number.Value <= matches.Count)
            {
                return matches[number.Value - 1];
            }
            throw new AssistantClarificationException("Работа с таким номером не найдена.", "lineNumber");
        }
        if (matches.Count != 1)
        {
            throw new AssistantClarificationException(matches.Count == 0 ? "Не нашёл такую работу в заказе." : "Уточните, какую из одинаковых работ изменить.", "lineNumber", OperationsAssistantLimits.Choices(options, matches.Select((item, index) => new AssistantChoice((index + 1).ToString(CultureInfo.InvariantCulture), $"{item.Name} — {item.Price:0.##} ₽"))));
        }
        return matches[0];
    }

    private static string GetMaintenanceLabel(GetMaintenanceByIdDto item) => $"Заказ от {item.Date:dd.MM.yyyy}, {item.Status}";
    private static string Normalize(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string Require(string? value, string field) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new AssistantClarificationException("Уточните госномер автомобиля.", field);
    private static Guid? GetGuid(AiPilotCommand command, string key, AssistantActionSelection? selection = null) => Guid.TryParse(GetString(command, key, selection), out var id) ? id : null;
    private static string? GetString(AiPilotCommand command, string key, AssistantActionSelection? selection = null) => command.Parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : selection?.CatalogChoices?.GetValueOrDefault(key);
    private static decimal? GetDecimal(AiPilotCommand command, string key) => command.Parameters.TryGetValue(key, out var value) && value.TryGetDecimal(out var number) ? number : null;
    private static int? GetInt(AiPilotCommand command, string key, AssistantActionSelection? selection = null) => command.Parameters.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : int.TryParse(selection?.CatalogChoices?.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var choice) ? choice : null;
    private static bool IsTrue(AiPilotCommand command, string key) => command.Parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTime? ParseOptionalDate(string? value) => string.IsNullOrWhiteSpace(value) ? null : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result) ? result : throw new AssistantClarificationException("Уточните дату в формате день, месяц и год.", "date");
    private static DateTime? ParseDate(string? value, string field) => ParseOptionalDate(value) ?? throw new AssistantClarificationException("Для новой записи нужна дата.", field);
    private static TimeSpan? ParseTime(string? value, string field) => string.IsNullOrWhiteSpace(value) ? null : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : throw new AssistantClarificationException("Уточните время в формате часы и минуты.", field);
    private static void EnsureSnapshot(string expected, string actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
        {
            throw new AssistantClarificationException("Данные изменились после подготовки действия. Проверьте запись ещё раз.", "refresh");
        }
    }

    private static string Fingerprint(params object?[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static string Fingerprint(GetMaintenanceByIdDto item) => Fingerprint(item.Id, item.Version, item.Status, item.Date, item.PlannedDate, item.PlannedStartTime, item.PlannedEndTime, item.CompletedDate, item.Comment, item.Price, item.PaidAmount, item.VehicleId, item.MaintenanceDetails);
    private static string Fingerprint(GetMaintenanceByIdDto item, IReadOnlyList<MaintenancePaymentDto> payments) => Fingerprint(Fingerprint(item), payments);
    private static string Fingerprint(GetVehicleDto item) => Fingerprint(item.Id, item.OrganizationId, item.RegistrationPlate, item.Name, item.Vin, item.YearIssue, item.Color, item.Description, item.OrganizationClientId, item.IsArchived);
    private static string Fingerprint(OrganizationClientDto item) => Fingerprint(item.Id, item.OrganizationId, item.Name, item.Phone, item.Comment, item.StatusCode, item.IsArchived);
}
