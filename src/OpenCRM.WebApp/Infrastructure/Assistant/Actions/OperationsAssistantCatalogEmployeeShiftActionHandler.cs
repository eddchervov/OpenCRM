using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

public sealed class OperationsAssistantCatalogEmployeeShiftActionHandler(
    ICatalogQueryService catalogQuery,
    ICatalogCommandService catalogCommand,
    IOrganizationEmployeeService employeeService,
    IOrganizationShiftService shiftService,
    IAssistantExecutionGuard executionGuard,
    AssistantOptions options) : IAssistantActionHandler
{
    private static readonly IReadOnlyDictionary<string, string> CatalogGroups = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["service"] = "services",
        ["service_bundle"] = "serviceBundles",
        ["work_post"] = "workPosts",
        ["work_type"] = "workTypes",
        ["document"] = "documents",
        ["planning_rule"] = "planningRules",
        ["material"] = "materials"
    };

    public IReadOnlyCollection<string> Actions { get; } =
    [
        "catalog_list", "catalog_create", "catalog_update", "catalog_delete",
        "location_list", "location_create", "location_update", "location_delete",
        "employee_list", "employee_create", "employee_update", "employee_deactivate", "employee_link",
        "shift_list", "shift_create", "shift_update", "shift_delete", "shift_publish", "shift_copy"
    ];

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Action.StartsWith("catalog_", StringComparison.Ordinal) || command.Action.StartsWith("location_", StringComparison.Ordinal))
        {
            return await this.PrepareCatalogAsync(command, actor, selection, cancellationToken);
        }

        if (command.Action.StartsWith("employee_", StringComparison.Ordinal))
        {
            return await this.PrepareEmployeeAsync(command, actor, selection, cancellationToken);
        }

        if (command.Action.StartsWith("shift_", StringComparison.Ordinal))
        {
            return await this.PrepareShiftAsync(command, actor, selection, cancellationToken);
        }

        throw new InvalidOperationException("Команда не поддерживается этим обработчиком.");
    }

    private async Task<AssistantPreparedAction> PrepareCatalogAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        var action = command.Action;
        var parameters = new OperationsAssistantParameters(command);
        var catalog = await catalogQuery.GetAsync(actor.UserId, actor.OrganizationId);
        var requestedKind = action.StartsWith("location_", StringComparison.Ordinal)
            ? "work_post"
            : parameters.OptionalText("catalogKind");
        if (requestedKind is not null && !CatalogGroups.ContainsKey(requestedKind))
        {
            throw new InvalidOperationException("Неизвестный вид справочника.");
        }

        var items = GetCatalogItems(catalog, requestedKind);
        if (action.EndsWith("_list", StringComparison.Ordinal))
        {
            var name = parameters.OptionalText("name");
            if (name is not null)
            {
                var listMatches = items.Where(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                if (listMatches.Length == 0)
                {
                    throw new InvalidOperationException("Позиция не найдена.");
                }

                var selectedItem = ResolveBySelection(listMatches, selection, "name", item => item.Id.ToString(), item => item.Name, null, "элемент справочника");
                items = [selectedItem];
            }

            return ReadOnly(
                requestedKind is null ? "Справочники организации" : $"Справочник {requestedKind}",
                items.Select(item => $"{item.Name}{(item.IsActive ? string.Empty : " (неактивно)")}").ToArray(),
                catalog);
        }

        var kind = requestedKind ?? parameters.RequiredText("catalogKind", 40);
        if (!CatalogGroups.ContainsKey(kind))
        {
            throw new InvalidOperationException("Этот вид справочника недоступен.");
        }

        if (action.EndsWith("_create", StringComparison.Ordinal))
        {
            var name = parameters.RequiredText("name", 150);
            var serviceIds = ResolveServiceIds(catalog, parameters.OptionalText("serviceNames"), selection);
            if (kind == "service_bundle" && serviceIds.Length == 0)
            {
                throw new InvalidOperationException("Назовите входящие в набор услуги через точку с запятой.");
            }

            var request = new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = kind,
                Name = name,
                Description = parameters.OptionalText("description"),
                UnitPrice = parameters.OptionalDecimal("unitPrice") ?? parameters.OptionalDecimal("price"),
                DurationMinutes = parameters.OptionalInt("durationMinutes"),
                ServiceIds = serviceIds,
                SortOrder = parameters.OptionalInt("sortOrder") ?? items.Count
            };
            return Mutation(
                $"Справочник {kind}",
                [$"Добавить «{name}»"],
                catalog,
                async ct =>
                {
                    await executionGuard.EnsureAllowedAsync(actor, action, ct);
                    await catalogCommand.CreateAsync(actor.UserId, actor.OrganizationId, request);
                    return new AssistantActionResult($"Добавлена позиция «{name}».");
                });
        }

        var item = ResolveBySelection(items, selection, "name", catalogItem => catalogItem.Id.ToString(), catalogItem => catalogItem.Name, parameters.RequiredText("name", 150), "элемент справочника");
        if (item.IsSystem)
        {
            throw new InvalidOperationException("Системный элемент справочника нельзя изменить через помощника.");
        }

        if (action.EndsWith("_delete", StringComparison.Ordinal))
        {
            if (item.IsUsed)
            {
                throw new InvalidOperationException("Справочник используется существующими заказами. Удаление невозможно.");
            }

            return Mutation(
                item.Name,
                [$"Удалить «{item.Name}» из справочника"],
                new { Catalog = catalog, Item = item },
                async ct =>
                {
                    await executionGuard.EnsureAllowedAsync(actor, action, ct);
                    await catalogCommand.DeleteAsync(actor.UserId, actor.OrganizationId, item.Id);
                    return new AssistantActionResult($"Позиция «{item.Name}» удалена.");
                },
                destructive: true);
        }

        var updateServiceIds = ResolveServiceIds(catalog, parameters.OptionalText("serviceNames"), selection) is { Length: > 0 } suppliedIds
            ? suppliedIds
            : item.ServiceIds;
        var updated = new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = parameters.OptionalText("newName") ?? item.Name,
            Description = parameters.Has("description") ? parameters.OptionalText("description") : item.Description,
            UnitPrice = parameters.OptionalDecimal("unitPrice") ?? parameters.OptionalDecimal("price") ?? item.UnitPrice,
            DurationMinutes = parameters.OptionalInt("durationMinutes") ?? item.DurationMinutes,
            ServiceIds = updateServiceIds,
            SortOrder = parameters.OptionalInt("sortOrder") ?? item.SortOrder,
            IsActive = parameters.OptionalBool("isActive") ?? item.IsActive,
            DocumentKindCode = item.DocumentKindCode
        };
        return Mutation(
            item.Name,
            [$"Изменить «{item.Name}»"],
            new { Catalog = catalog, Item = item },
            async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await catalogCommand.UpdateAsync(actor.UserId, actor.OrganizationId, item.Id, updated);
                return new AssistantActionResult($"Позиция «{item.Name}» обновлена.");
            });
    }

    private async Task<AssistantPreparedAction> PrepareEmployeeAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        var action = command.Action;
        var parameters = new OperationsAssistantParameters(command);
        var employees = await employeeService.GetAsync(actor.UserId, actor.OrganizationId);
        if (action == "employee_list")
        {
            return ReadOnly("Сотрудники организации", employees.Select(employee => $"{employee.Name}{(employee.IsActive ? string.Empty : " (неактивен)")}").ToArray(), employees);
        }

        if (action == "employee_create")
        {
            var name = parameters.RequiredTextEither("name", "newName", 150);
            var employee = new OrganizationEmployeeDto
            {
                Name = name,
                Phone = parameters.OptionalText("phone"),
                IsActive = parameters.OptionalBool("isActive") ?? true
            };
            return Mutation(
                "Сотрудники организации",
                [$"Добавить сотрудника {name}"],
                employees,
                async ct =>
                {
                    await executionGuard.EnsureAllowedAsync(actor, action, ct);
                    await employeeService.SaveAsync(actor.UserId, actor.OrganizationId, null, employee);
                    return new AssistantActionResult($"Сотрудник {name} добавлен.");
                });
        }

        var nameValue = parameters.RequiredText("employeeName", 150);
        var current = ResolveBySelection(employees, selection, "employeeName", employee => employee.Id.ToString(), employee => employee.Name, nameValue, "сотрудника");
        if (action == "employee_deactivate")
        {
            if (!current.IsActive)
            {
                throw new InvalidOperationException("Этот сотрудник уже неактивен.");
            }

            var inactive = CopyEmployee(current, isActive: false);
            return Mutation(current.Name, ["Сделать сотрудника неактивным"], employees, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await employeeService.SaveAsync(actor.UserId, actor.OrganizationId, current.Id, inactive);
                return new AssistantActionResult($"Сотрудник {current.Name} теперь неактивен.");
            }, destructive: true);
        }

        if (action == "employee_link")
        {
            if (!parameters.Has("memberEmail"))
            {
                throw new InvalidOperationException("Назовите email участника или явно попросите снять связь.");
            }

            var memberOptions = await employeeService.GetMemberOptionsAsync(actor.UserId, actor.OrganizationId);
            var memberEmail = parameters.OptionalText("memberEmail");
            var member = memberEmail is null
                ? null
                : ResolveBySelection(memberOptions, selection, "memberEmail", option => option.Id.ToString(), option => option.Email ?? option.UserName ?? string.Empty, memberEmail, "участника");
            return Mutation(current.Name, [$"Связать сотрудника с {memberEmail ?? "участником не связан"}"], new { Employees = employees, Members = memberOptions }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await employeeService.LinkMemberAsync(actor.UserId, actor.OrganizationId, current.Id, member?.Id, current.Version);
                return new AssistantActionResult(member is null
                    ? $"Связь сотрудника {current.Name} снята."
                    : $"Сотрудник {current.Name} связан с участником.");
            }, destructive: true);
        }

        var updated = new OrganizationEmployeeDto
        {
            Id = current.Id,
            Name = parameters.OptionalText("newName") ?? current.Name,
            Phone = parameters.Has("phone") ? parameters.OptionalText("phone") : current.Phone,
            MemberId = current.MemberId,
            IsActive = parameters.OptionalBool("isActive") ?? current.IsActive,
            Version = current.Version
        };
        return Mutation(current.Name, [$"Изменить данные сотрудника {current.Name}"], employees, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await employeeService.SaveAsync(actor.UserId, actor.OrganizationId, current.Id, updated);
            return new AssistantActionResult($"Данные сотрудника {current.Name} обновлены.");
        });
    }

    private async Task<AssistantPreparedAction> PrepareShiftAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        var parameters = new OperationsAssistantParameters(command);
        var date = parameters.OptionalDate("date") ?? parameters.OptionalDateFromCommand() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var week = await shiftService.GetWeekAsync(actor.UserId, actor.OrganizationId, weekStart);
        var action = command.Action;
        if (action == "shift_list")
        {
            return ReadOnly($"Смены с {weekStart:yyyy-MM-dd}", week.Shifts.Select(shift => $"{shift.EmployeeName}: {shift.StartLocal} — {shift.EndLocal}").ToArray(), week);
        }

        if (action == "shift_publish" || action == "shift_copy")
        {
            return Mutation($"Неделя с {weekStart:yyyy-MM-dd}", [action == "shift_publish" ? "Опубликовать смены" : "Скопировать смены предыдущей недели"], week, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                var count = action == "shift_publish"
                    ? await shiftService.PublishWeekAsync(actor.UserId, actor.OrganizationId, weekStart)
                    : await shiftService.CopyPreviousWeekAsync(actor.UserId, actor.OrganizationId, weekStart);
                return new AssistantActionResult($"Выполнено. Обработано смен: {count}.");
            }, destructive: true);
        }

        var employeeName = parameters.RequiredText("employeeName", 150);
        var employee = ResolveBySelection(week.Employees.Where(item => item.IsActive).ToArray(), selection, "employeeName", item => item.Id.ToString(), item => item.Name, employeeName, "активного сотрудника");
        if (action == "shift_create")
        {
            var start = LocalDateTime(date, parameters.RequiredTimeEither("startTime", command.StartTime));
            var end = LocalDateTime(date, parameters.RequiredTimeEither("endTime", command.EndTime));
            EnsureValidShift(start, end, week.Shifts, employee.Id, null);
            var request = new SaveOrganizationShiftDto
            {
                EmployeeId = employee.Id,
                StartLocal = start,
                EndLocal = end,
                PlannedBreakMinutes = parameters.OptionalInt("plannedBreakMinutes") ?? 0,
                Reason = parameters.OptionalText("reason")
            };
            return Mutation(employee.Name, [$"Создать смену {start:yyyy-MM-dd HH:mm} — {end:HH:mm}"], week, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await shiftService.SaveAsync(actor.UserId, actor.OrganizationId, null, request);
                return new AssistantActionResult($"Смена для {employee.Name} создана.");
            });
        }

        var candidates = week.Shifts.Where(shift => shift.EmployeeId == employee.Id && shift.LocalStartDate == date && !shift.IsCancelled).ToArray();
        var shift = ResolveShift(candidates, selection, parameters.OptionalInt("shiftNumber"));
        if (action == "shift_delete")
        {
            var reason = parameters.OptionalText("reason") ?? "Изменение графика";
            return Mutation($"{employee.Name}, {shift.StartLocal} — {shift.EndLocal}", [$"Отменить смену: {reason}"], week, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, action, ct);
                await shiftService.CancelAsync(actor.UserId, actor.OrganizationId, shift.Id, shift.Version, reason);
                return new AssistantActionResult($"Смена {employee.Name} отменена.");
            }, destructive: true);
        }

        var startTime = parameters.OptionalText("startTime") ?? shift.StartLocal[11..16];
        var endTime = parameters.OptionalText("endTime") ?? shift.EndLocal[11..16];
        var updatedStart = LocalDateTime(date, startTime);
        var updatedEnd = LocalDateTime(date, endTime);
        EnsureValidShift(updatedStart, updatedEnd, week.Shifts, employee.Id, shift.Id);
        var update = new SaveOrganizationShiftDto
        {
            EmployeeId = shift.EmployeeId,
            StartLocal = updatedStart,
            EndLocal = updatedEnd,
            PlannedBreakMinutes = parameters.OptionalInt("plannedBreakMinutes") ?? shift.PlannedBreakMinutes,
            Version = shift.Version,
            Reason = parameters.OptionalText("reason")
        };
        return Mutation($"{employee.Name}, {date:yyyy-MM-dd}", [$"Изменить смену на {updatedStart:HH:mm} — {updatedEnd:HH:mm}"], week, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, action, ct);
            await shiftService.SaveAsync(actor.UserId, actor.OrganizationId, shift.Id, update);
            return new AssistantActionResult($"Смена {employee.Name} обновлена.");
        });
    }

    private static IReadOnlyList<OrganizationScenarioCatalogItemDto> GetCatalogItems(OrganizationScenarioCatalogDto catalog, string? kind)
    {
        if (kind is null)
        {
            return catalog.WorkTypes.Concat(catalog.Services).Concat(catalog.ServiceBundles).Concat(catalog.WorkPosts)
                .Concat(catalog.Documents).Concat(catalog.PlanningRules).Concat(catalog.Materials).ToArray();
        }

        return kind switch
        {
            "service" => catalog.Services,
            "service_bundle" => catalog.ServiceBundles,
            "work_post" => catalog.WorkPosts,
            "work_type" => catalog.WorkTypes,
            "document" => catalog.Documents,
            "planning_rule" => catalog.PlanningRules,
            "material" => catalog.Materials,
            _ => []
        };
    }

    private Guid[] ResolveServiceIds(
        OrganizationScenarioCatalogDto catalog,
        string? names,
        AssistantActionSelection selection)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return [];
        }

        return names.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((name, index) => ResolveBundleService(catalog.Services, selection, name, index))
            .ToArray();
    }

    private Guid ResolveBundleService(
        IReadOnlyList<OrganizationScenarioCatalogItemDto> services,
        AssistantActionSelection selection,
        string name,
        int index)
    {
        var matches = OperationsAssistantLimits.Candidates(options, services
            .Where(item => item.IsActive && string.Equals(item.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase))
        );
        var field = $"bundleService:{index}:{name}";

        if (TryGetSelection(selection, field, out var selectedId))
        {
            return matches.FirstOrDefault(item => item.Id.ToString() == selectedId)?.Id
                ?? throw new InvalidOperationException($"Выбранная активная услуга «{name}» больше не найдена.");
        }

        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Активная услуга «{name}» не найдена.");
        }

        if (matches.Length > 1)
        {
            throw new AssistantClarificationException(
                $"Найдено несколько активных услуг «{name}». Выберите нужную.",
                field,
                OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), $"{item.Name} · {FormatMoney(item.UnitPrice, item.CurrencyCode)}"))));
        }

        return matches[0].Id;
    }

    private static OrganizationEmployeeDto CopyEmployee(OrganizationEmployeeDto employee, bool isActive) => new()
    {
        Id = employee.Id,
        Name = employee.Name,
        Phone = employee.Phone,
        MemberId = employee.MemberId,
        IsActive = isActive,
        Version = employee.Version
    };

    private static DateTime LocalDateTime(DateOnly date, string time)
    {
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
        {
            throw new InvalidOperationException("Укажите время в формате ЧЧ:ММ.");
        }

        return date.ToDateTime(parsedTime, DateTimeKind.Unspecified);
    }

    private static string FormatMoney(decimal? amount, string currencyCode) => amount.HasValue
        ? $"{amount.Value.ToString("0.##", CultureInfo.InvariantCulture)} {currencyCode}"
        : "цена не указана";

    private static void EnsureValidShift(DateTime start, DateTime end, IEnumerable<OrganizationShiftDto> shifts, Guid employeeId, Guid? exceptShiftId)
    {
        if (end <= start)
        {
            throw new InvalidOperationException("Время окончания смены должно быть позже начала.");
        }

        if (shifts.Any(shift => shift.Id != exceptShiftId && shift.EmployeeId == employeeId && !shift.IsCancelled
            && DateTime.ParseExact(shift.StartLocal, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) < end
            && DateTime.ParseExact(shift.EndLocal, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) > start))
        {
            throw new InvalidOperationException("Смена пересекается с уже созданной сменой этого сотрудника.");
        }
    }

    private OrganizationShiftDto ResolveShift(IReadOnlyList<OrganizationShiftDto> candidates, AssistantActionSelection selection, int? number)
    {
        if (TryGetSelection(selection, "shiftNumber", out var selectedId))
        {
            return candidates.FirstOrDefault(shift => shift.Id.ToString() == selectedId)
                ?? throw new InvalidOperationException("Выбранная смена больше не найдена.");
        }

        if (number is null)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            throw new AssistantClarificationException("Укажите, какую смену нужно изменить.", "shiftNumber", OperationsAssistantLimits.Choices(options, candidates.Select((shift, index) => new AssistantChoice(shift.Id.ToString(), $"{index + 1}. {shift.StartLocal} — {shift.EndLocal}"))));
        }

        if (number < 1 || number > candidates.Count)
        {
            throw new InvalidOperationException("Номер смены не найден.");
        }

        return candidates[number.Value - 1];
    }

    private T ResolveBySelection<T>(
        IReadOnlyList<T> items,
        AssistantActionSelection selection,
        string field,
        Func<T, string> getId,
        Func<T, string> getLabel,
        string? requestedLabel,
        string concept)
    {
        if (TryGetSelection(selection, field, out var selectedId))
        {
            return items.FirstOrDefault(item => string.Equals(getId(item), selectedId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Выбранный вариант {concept} больше не найден.");
        }

        var matches = OperationsAssistantLimits.Candidates(options, requestedLabel is null
            ? items
            : items.Where(item => string.Equals(getLabel(item).Trim(), requestedLabel.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"{concept} «{requestedLabel}» не найден.");
        }

        throw new AssistantClarificationException(
            $"Найдено несколько совпадений для {concept}. Выберите нужный вариант.",
            field,
            OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(getId(item), getLabel(item)))));
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

    private static AssistantPreparedAction Mutation(
        string target,
        IReadOnlyList<string> changes,
        object snapshot,
        Func<CancellationToken, Task<AssistantActionResult>> execute,
        bool destructive = false)
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

    private static string Fingerprint(object value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }
}
