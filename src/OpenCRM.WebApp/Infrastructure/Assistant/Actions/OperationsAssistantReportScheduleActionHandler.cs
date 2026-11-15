using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

public sealed class OperationsAssistantReportScheduleActionHandler(
    IWorkspaceAccessService workspaceAccess,
    IOrganizationWorkspaceMaintenanceQueryService maintenanceQuery,
    IOrganizationFinanceQueryService financeQuery) : IAssistantActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["report", "schedule"];

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await workspaceAccess.GetWorkspaceAccessAsync(actor.UserId, actor.OrganizationId);

        var parameters = new OperationsAssistantParameters(command);
        var from = parameters.OptionalDate("dateFrom") ?? parameters.OptionalDateFromCommand() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var to = parameters.OptionalDate("dateTo") ?? from;
        if (from > to || to.DayNumber - from.DayNumber > 366)
        {
            throw new InvalidOperationException("Укажите корректный период не длиннее одного года.");
        }

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MaxValue);
        if (command.Action == "schedule")
        {
            var items = await maintenanceQuery.GetMaintenanceCalendarAsync(actor.OrganizationId, start, end, includeAllStatuses: true);
            return ReadOnly(
                $"Расписание {from:yyyy-MM-dd} — {to:yyyy-MM-dd}",
                items.Select(item => new
                {
                    Дата = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Начало = item.PlannedStartTime?.ToString("hh\\:mm"),
                    Окончание = item.PlannedEndTime?.ToString("hh\\:mm"),
                    Автомобиль = item.RegistrationPlate ?? item.VehicleDisplayName,
                    Работы = string.Join(", ", item.ServiceNames),
                    Пост = item.Location,
                    Статус = item.StatusName,
                    Стоимость = item.Price
                }).ToArray(),
                $"Расписание за {from:yyyy-MM-dd} — {to:yyyy-MM-dd}.");
        }

        var finance = await financeQuery.GetFinanceSummaryAsync(actor.OrganizationId, start, end);
        var kpis = finance.Kpis;
        var summary = new
        {
            Заказы = kpis.OrdersCount,
            Начислено = kpis.NetAmount,
            Получено = kpis.PaidAmount,
            Долг = kpis.DebtAmount,
            Расходы = kpis.CostAmount,
            Прибыль = kpis.ProfitAmount,
            Скидки = kpis.DiscountAmount
        };
        return ReadOnly($"Финансы {from:yyyy-MM-dd} — {to:yyyy-MM-dd}", summary, $"Отчёт за {from:yyyy-MM-dd} — {to:yyyy-MM-dd}.");
    }

    private static AssistantPreparedAction ReadOnly(string target, object data, string message)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = [],
            Snapshot = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)))),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(data),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult(message))
        };
    }
}
