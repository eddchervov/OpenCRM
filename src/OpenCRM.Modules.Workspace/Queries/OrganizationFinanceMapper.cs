using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Domain;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Преобразование и расчёты финансовых данных организации. </summary>
internal static class OrganizationFinanceMapper
{
    /// <summary> Округляет денежную сумму. </summary>
    internal static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary> Определяет статус оплаты по суммам и стоимости строк обслуживания. </summary>
    internal static string ResolvePaymentStatus(
        decimal paidAmount,
        decimal netAmount,
        bool hasPricedDetail = false,
        bool areAllDetailsPriced = false) =>
        MaintenancePaymentStatus.Resolve(paidAmount, netAmount, hasPricedDetail, areAllDetailsPriced);

    /// <summary> Возвращает название статуса оплаты по коду. </summary>
    internal static string ResolvePaymentStatusName(string status) => status switch
    {
        "unpaid" => "Не оплачено",
        "partial" => "Частично",
        "paid" => "Оплачено",
        "overpaid" => "Переплата",
        MaintenancePaymentStatus.NoPaymentRequired => "Оплата не требуется",
        _ => "Неизвестно"
    };

    /// <summary> Формирует список денежных показателей по переданным элементам. </summary>
    internal static IReadOnlyList<OrganizationFinanceBreakdownItemDto> BuildMoneyBreakdown(
        IReadOnlyList<(string Code, string Label, decimal Value)> items)
    {
        var total = items.Sum(item => item.Value);

        return items
            .Where(item => item.Value > 0)
            .Select(item => new OrganizationFinanceBreakdownItemDto
            {
                Code = item.Code,
                Label = item.Label,
                Value = item.Value,
                Percent = total <= 0 ? 0 : (int)Math.Round(item.Value * 100m / total)
            })
            .ToList();
    }

    /// <summary> Возвращает последовательность дней в диапазоне дат. </summary>
    internal static IEnumerable<DateTime> EachDay(DateTime from, DateTime to)
    {
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
