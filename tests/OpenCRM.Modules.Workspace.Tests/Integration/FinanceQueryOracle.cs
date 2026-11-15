using Microsoft.EntityFrameworkCore;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

/// <summary> Эталон исходных расчётов финансов для проверки агрегатов на PostgreSQL. </summary>
internal sealed class FinanceQueryOracle(OpenCRMContext opencrmContext) : IOrganizationFinanceQueryService
{
    /// <summary> Возвращает финансовую сводку организации за период. </summary>
    public async Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var toExclusive = to.AddDays(1);
        var dates = OrganizationFinanceMapper.EachDay(from, to).ToList();
        var maintenanceRows = await opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => x.Vehicle.OrganizationId == organizationId && !x.IsDeleted && !x.Vehicle.IsDeleted)
            .Where(x => (x.CompletedDate ?? x.PlannedDate ?? x.Date) >= from && (x.CompletedDate ?? x.PlannedDate ?? x.Date) < toExclusive)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Brand)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Model)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.OrganizationClient)
            .OrderByDescending(x => x.CompletedDate ?? x.PlannedDate ?? x.Date)
            .ToListAsync();

        var rows = maintenanceRows.Select(x =>
        {
            var details = x.Details;
            var vehicleClientName = x.Vehicle.OrganizationClient?.Name;
            var buyerClientId = x.BuyerClientId;
            var buyerName = buyerClientId.HasValue ? x.BuyerName : null;
            var hasCatalogIdentity = x.Vehicle.BrandId.HasValue || x.Vehicle.ModelId.HasValue;
            var grossAmount = OrganizationFinanceMapper.RoundMoney(details.Sum(detail => (decimal)(detail.Price ?? 0)));
            var discountAmount = OrganizationFinanceMapper.RoundMoney(x.DiscountAmount);
            var isCancelled = x.Status == OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled;
            var netAmount = isCancelled ? 0m : Math.Max(0m, grossAmount - discountAmount);
            var paidAmount = OrganizationFinanceMapper.RoundMoney(x.PaidAmount);
            var debtAmount = isCancelled ? 0m : Math.Max(0m, netAmount - paidAmount);
            var materialCostAmount = OrganizationFinanceMapper.RoundMoney(x.MaterialCostAmount);
            var laborCostAmount = OrganizationFinanceMapper.RoundMoney(x.LaborCostAmount);
            var profitAmount = OrganizationFinanceMapper.RoundMoney(netAmount - materialCostAmount - laborCostAmount);
            var paymentStatus = OrganizationFinanceMapper.ResolvePaymentStatus(
                paidAmount,
                netAmount,
                isCancelled || details.Any(detail => detail.Price.HasValue),
                isCancelled || details.All(detail => detail.Price.HasValue));

            return new FinanceMaintenanceRow
            {
                Id = x.Id,
                IsCancelled = isCancelled,
                VehicleId = x.VehicleId,
                Date = (x.CompletedDate ?? x.PlannedDate ?? x.Date).Date,
                GrossAmount = grossAmount,
                DiscountAmount = discountAmount,
                NetAmount = netAmount,
                PaidAmount = paidAmount,
                DebtAmount = debtAmount,
                MaterialCostAmount = materialCostAmount,
                LaborCostAmount = laborCostAmount,
                ProfitAmount = profitAmount,
                PaymentStatus = paymentStatus,
                VehicleDisplayName = OrganizationWorkspaceMaintenanceMapper.GetVehicleDisplayName(x.Vehicle),
                VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(x.Vehicle.Name, x.Vehicle.YearIssue, vehicleClientName, x.Vehicle.RegistrationPlate, !hasCatalogIdentity),
                ClientId = buyerClientId,
                ClientName = buyerName,
                Details = details
                    .Select(detail => new FinanceMaintenanceDetailRow
                    {
                        Name = detail.Name,
                        Amount = OrganizationFinanceMapper.RoundMoney((decimal)(detail.Price ?? 0))
                    })
                    .ToList()
            };
        }).ToList();

        var kpis = new OrganizationFinanceKpisDto
        {
            OrdersCount = rows.Count(x => !x.IsCancelled),
            GrossAmount = rows.Where(x => !x.IsCancelled).Sum(x => x.GrossAmount),
            NetAmount = rows.Where(x => !x.IsCancelled).Sum(x => x.NetAmount),
            PaidAmount = rows.Sum(x => x.PaidAmount),
            DebtAmount = rows.Where(x => !x.IsCancelled).Sum(x => x.DebtAmount),
            DiscountAmount = rows.Where(x => !x.IsCancelled).Sum(x => x.DiscountAmount),
            MaterialCostAmount = rows.Sum(x => x.MaterialCostAmount),
            LaborCostAmount = rows.Sum(x => x.LaborCostAmount),
            CostAmount = rows.Sum(x => x.MaterialCostAmount + x.LaborCostAmount),
            ProfitAmount = rows.Sum(x => x.ProfitAmount),
            AverageCheckAmount = rows.All(x => x.IsCancelled) ? 0m
                : OrganizationFinanceMapper.RoundMoney(rows.Where(x => !x.IsCancelled).Sum(x => x.NetAmount) / rows.Count(x => !x.IsCancelled))
        };

        return new OrganizationFinanceSummaryDto
        {
            DateFrom = from,
            DateTo = to,
            Kpis = kpis,
            RevenueSeries = dates.Select(date => new OrganizationFinanceMoneySeriesPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = rows.Where(x => x.Date == date).Sum(x => x.PaidAmount)
            }).ToList(),
            AccruedSeries = dates.Select(date => new OrganizationFinanceMoneySeriesPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = rows.Where(x => x.Date == date && !x.IsCancelled).Sum(x => x.NetAmount)
            }).ToList(),
            CostBreakdown = OrganizationFinanceMapper.BuildMoneyBreakdown([
                ("materials", "Материалы", kpis.MaterialCostAmount),
                ("labor", "Труд", kpis.LaborCostAmount),
                ("discount", "Скидки", kpis.DiscountAmount)
            ]),
            PaymentBreakdown = OrganizationFinanceMapper.BuildMoneyBreakdown([
                ("paid", "Оплачено", kpis.PaidAmount),
                ("debt", "Долг", kpis.DebtAmount)
            ]),
            TopServices = rows
                .Where(x => !x.IsCancelled)
                .SelectMany(x => x.Details)
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Amount > 0)
                .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => new OrganizationFinanceServiceItemDto
                {
                    Name = x.Key,
                    Count = x.Count(),
                    Amount = x.Sum(item => item.Amount)
                })
                .OrderByDescending(x => x.Amount)
                .ThenBy(x => x.Name)
                .Take(8)
                .ToList(),
            TopDebtors = rows
                .Where(x => x.DebtAmount > 0)
                .GroupBy(x => new { x.ClientId, Name = string.IsNullOrWhiteSpace(x.ClientName) ? "Клиент не указан" : x.ClientName.Trim() })
                .Select(x => new OrganizationFinanceClientDebtItemDto
                {
                    ClientId = x.Key.ClientId,
                    ClientName = x.Key.Name,
                    OrdersCount = x.Count(),
                    DebtAmount = x.Sum(item => item.DebtAmount)
                })
                .OrderByDescending(x => x.DebtAmount)
                .ThenBy(x => x.ClientName)
                .Take(8)
                .ToList(),
            Orders = rows
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .Take(80)
                .Select(x => new OrganizationFinanceOrderItemDto
                {
                    Id = x.Id,
                    IsCancelled = x.IsCancelled,
                    Date = x.Date,
                    VehicleId = x.VehicleId,
                    VehicleDisplayName = x.VehicleDisplayName,
                    VehicleSubtitle = x.VehicleSubtitle,
                    ClientId = x.ClientId,
                    ClientName = x.ClientName,
                    GrossAmount = x.GrossAmount,
                    NetAmount = x.NetAmount,
                    PaidAmount = x.PaidAmount,
                    DebtAmount = x.DebtAmount,
                    DiscountAmount = x.DiscountAmount,
                    MaterialCostAmount = x.MaterialCostAmount,
                    LaborCostAmount = x.LaborCostAmount,
                    ProfitAmount = x.ProfitAmount,
                    PaymentStatus = x.PaymentStatus,
                    PaymentStatusName = OrganizationFinanceMapper.ResolvePaymentStatusName(x.PaymentStatus),
                    DetailUrl = $"#maintenance/{x.Id}"
                })
                .ToList()
        };
    }

    /// <summary> Строка обслуживания для финансовой сводки. </summary>
    private sealed class FinanceMaintenanceRow
    {
        /// <summary> Идентификатор записи обслуживания. </summary>
        public Guid Id { get; init; }

        public bool IsCancelled { get; init; }

        /// <summary> Идентификатор транспортного средства. </summary>
        public Guid VehicleId { get; init; }

        /// <summary> Дата обслуживания. </summary>
        public DateTime Date { get; init; }

        /// <summary> Сумма до скидки. </summary>
        public decimal GrossAmount { get; init; }

        /// <summary> Сумма скидки. </summary>
        public decimal DiscountAmount { get; init; }

        /// <summary> Сумма после скидки. </summary>
        public decimal NetAmount { get; init; }

        /// <summary> Оплаченная сумма. </summary>
        public decimal PaidAmount { get; init; }

        /// <summary> Сумма долга. </summary>
        public decimal DebtAmount { get; init; }

        /// <summary> Стоимость материалов. </summary>
        public decimal MaterialCostAmount { get; init; }

        /// <summary> Стоимость работ. </summary>
        public decimal LaborCostAmount { get; init; }

        /// <summary> Сумма прибыли. </summary>
        public decimal ProfitAmount { get; init; }

        /// <summary> Статус оплаты. </summary>
        public string PaymentStatus { get; init; } = string.Empty;

        /// <summary> Отображаемое название транспортного средства. </summary>
        public string VehicleDisplayName { get; init; } = string.Empty;

        /// <summary> Дополнительное описание транспортного средства. </summary>
        public string? VehicleSubtitle { get; init; }

        /// <summary> Идентификатор клиента. </summary>
        public Guid? ClientId { get; init; }

        /// <summary> Имя клиента. </summary>
        public string? ClientName { get; init; }

        /// <summary> Строки работ или услуг обслуживания. </summary>
        public IReadOnlyList<FinanceMaintenanceDetailRow> Details { get; init; } = [];
    }

    /// <summary> Строка работы или услуги для финансовой сводки. </summary>
    private sealed class FinanceMaintenanceDetailRow
    {
        /// <summary> Название работы или услуги. </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary> Сумма по строке. </summary>
        public decimal Amount { get; init; }
    }
}
