using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Commands;

/// <summary> Управляет оплатами по обслуживанию. </summary>
internal sealed class MaintenancePaymentService(
    IMaintenanceRepository maintenanceRepository,
    ISystemClock systemClock,
    OpenCRMContext context,
    MaintenanceOrderLoader maintenanceOrderLoader,
    MaintenanceAccess maintenanceAccess,
    MaintenanceHistoryWriter maintenanceHistoryWriter,
    MaintenanceBuyerResolver maintenanceBuyerResolver,
    MaintenanceTransaction transaction) : IMaintenancePaymentService
{
    /// <summary> Загрузчик записи обслуживания с блокировкой. </summary>
    private readonly MaintenanceOrderLoader _maintenanceOrderLoader = maintenanceOrderLoader;

    /// <summary> Проверка доступа к обслуживанию. </summary>
    private readonly MaintenanceAccess _maintenanceAccess = maintenanceAccess;

    /// <summary> Запись событий в историю обслуживания. </summary>
    private readonly MaintenanceHistoryWriter _maintenanceHistoryWriter = maintenanceHistoryWriter;

    /// <summary> Определение плательщика по обслуживанию. </summary>
    private readonly MaintenanceBuyerResolver _maintenanceBuyerResolver = maintenanceBuyerResolver;

    /// <summary> Транзакция сохранения изменений. </summary>
    private readonly MaintenanceTransaction _transaction = transaction;

    /// <summary> Возвращает оплаты по обслуживанию. </summary>
    public async Task<IReadOnlyList<MaintenancePaymentDto>> GetPayments(string userId, Guid organizationId, Guid maintenanceId)
    {
        await this._maintenanceAccess.EnsureCanReadWorkspaceAsync(userId, organizationId);
        _ = await maintenanceRepository.GetById(userId, organizationId, maintenanceId)
            ?? throw new InvalidOperationException("Обслуживание не найдено.");

        return await context.MaintenancePayments.AsNoTracking()
            .Where(payment => payment.MaintenanceId == maintenanceId)
            .OrderByDescending(payment => payment.CreatedAt).ThenBy(payment => payment.Id)
            .Select(payment => new MaintenancePaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                CurrencyCode = payment.CurrencyCode,
                MethodCode = payment.MethodCode,
                PayerClientId = payment.PayerClientId,
                PayerName = payment.PayerName,
                CreatedAt = payment.CreatedAt,
                CreatedBy = payment.CreatedBy,
                Comment = payment.Comment,
                IsOpeningBalance = payment.IsOpeningBalance,
                CancelledAt = payment.CancelledAt,
                CancelledBy = payment.CancelledBy,
                CancellationReason = payment.CancellationReason
            }).ToListAsync();
    }

    /// <summary> Добавляет оплату по обслуживанию. </summary>
    public Task AddPayment(string userId, Guid organizationId, Guid maintenanceId, AddMaintenancePaymentDto dto) =>
        this._transaction.ExecuteAsync(
            () => this.AddPaymentCoreAsync(userId, organizationId, maintenanceId, dto),
            () => context.MaintenancePayments.AsNoTracking().AnyAsync(payment =>
                payment.Id == dto.Id && payment.MaintenanceId == maintenanceId));

    /// <summary> Выполняет добавление оплаты. </summary>
    private async Task AddPaymentCoreAsync(string userId, Guid organizationId, Guid maintenanceId, AddMaintenancePaymentDto dto)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        if (dto.Id == Guid.Empty || dto.Amount <= 0 || dto.Amount > 999999999 || decimal.Round(dto.Amount, 2) != dto.Amount)
        {
            throw new MaintenancePaymentValidationException(
                "maintenance.payment.amountInvalid",
                "Укажите сумму оплаты больше нуля, не более двух знаков после запятой.");
        }
        var comment = MaintenanceTextRules.NormalizeOptional(dto.Comment);
        var methodCode = MaintenanceTextRules.NormalizeOptional(dto.MethodCode)?.ToLowerInvariant();
        if (methodCode is not (null or "cash" or "card" or "bank" or "other"))
        {
            throw new MaintenancePaymentValidationException(
                "maintenance.payment.methodInvalid",
                "Выберите корректный способ оплаты.");
        }

        var maintenance = await this._maintenanceOrderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
        var organizationCurrencyCode = await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync();
        MaintenanceCurrencyRules.EnsureMatchesOrganizationCurrency(maintenance, organizationCurrencyCode);
        var existing = await context.MaintenancePayments.AsNoTracking().SingleOrDefaultAsync(payment => payment.Id == dto.Id);
        if (existing != null)
        {
            if (existing.MaintenanceId != maintenanceId || existing.Amount != dto.Amount
                || existing.Comment != comment || existing.MethodCode != methodCode
                || dto.PayerClientId.HasValue && !Nullable.Equals(existing.PayerClientId, dto.PayerClientId))
            {
                throw new InvalidOperationException("Этот запрос оплаты уже использован с другими данными.");
            }

            return;
        }
        if (maintenance.Status == MaintenanceStatus.Cancelled)
        {
            throw new InvalidOperationException("Нельзя принимать оплату отменённой записи.");
        }
        if (dto.ExpectedPaidAmount != maintenance.PaidAmount)
        {
            throw new InvalidOperationException("Оплаты уже изменились. Обновите карточку и проверьте историю оплат.");
        }
        var finance = MaintenanceMoneyRules.BuildFinanceSummary(maintenance);
        if (dto.Amount > finance.DebtAmount)
        {
            var formattedDebt = MaintenanceChangeRules.FormatDecimalMoney(finance.DebtAmount, maintenance.CurrencyCode);
            throw new MaintenancePaymentValidationException(
                "maintenance.payment.overpayment",
                $"Сумма больше остатка к оплате: {formattedDebt}. Обновите карточку.",
                formattedDebt);
        }

        var payment = new MaintenancePayment
        {
            Id = dto.Id,
            MaintenanceId = maintenanceId,
            Amount = dto.Amount,
            CurrencyCode = maintenance.CurrencyCode,
            MethodCode = methodCode,
            Comment = comment,
            CreatedAt = systemClock.UtcNow,
            CreatedBy = await this._maintenanceHistoryWriter.ResolveActorDisplayNameAsync(userId)
        };
        await this._maintenanceBuyerResolver.SetPayerAsync(payment, maintenance, organizationId, dto.PayerClientId);
        context.MaintenancePayments.Add(payment);
        maintenance.PaidAmount += dto.Amount;
        await this._maintenanceHistoryWriter.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenanceId,
            "payment_received", "Учтена оплата", $"{MaintenanceChangeRules.FormatDecimalMoney(dto.Amount, payment.CurrencyCode)}. {comment}".Trim());
    }

    /// <summary> Отменяет оплату по обслуживанию с указанием причины. </summary>
    public Task CancelPayment(string userId, Guid organizationId, Guid maintenanceId, Guid paymentId, string reason) =>
        this._transaction.ExecuteAsync(
            () => this.CancelPaymentCoreAsync(userId, organizationId, maintenanceId, paymentId, reason),
            () => context.MaintenancePayments.AsNoTracking().AnyAsync(payment =>
                payment.Id == paymentId && payment.MaintenanceId == maintenanceId && payment.CancelledAt != null));

    /// <summary> Выполняет отмену оплаты. </summary>
    private async Task CancelPaymentCoreAsync(string userId, Guid organizationId, Guid maintenanceId, Guid paymentId, string reason)
    {
        await this._transaction.LockOrganizationAsync(organizationId);
        await this._maintenanceAccess.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        var normalizedReason = MaintenanceTextRules.NormalizeOptional(reason);
        if (normalizedReason == null)
        {
            throw new InvalidOperationException("Укажите причину отмены оплаты, не более 1000 символов.");
        }

        var maintenance = await this._maintenanceOrderLoader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
        var organizationCurrencyCode = await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync();
        MaintenanceCurrencyRules.EnsureMatchesOrganizationCurrency(maintenance, organizationCurrencyCode);
        var payment = await context.MaintenancePayments.SingleOrDefaultAsync(item => item.Id == paymentId && item.MaintenanceId == maintenanceId)
            ?? throw new InvalidOperationException("Оплата не найдена.");
        if (payment.CancelledAt.HasValue)
        {
            return;
        }
        if (maintenance.PaidAmount < payment.Amount)
        {
            throw new InvalidOperationException("Сумма оплат не совпадает с историей. Обратитесь в поддержку.");
        }

        payment.CancelledAt = systemClock.UtcNow;
        payment.CancelledBy = await this._maintenanceHistoryWriter.ResolveActorDisplayNameAsync(userId);
        payment.CancellationReason = normalizedReason;
        maintenance.PaidAmount -= payment.Amount;
        await this._maintenanceHistoryWriter.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenanceId,
            "payment_cancelled", "Отменён учёт оплаты", $"{MaintenanceChangeRules.FormatDecimalMoney(payment.Amount, payment.CurrencyCode)}. Причина: {normalizedReason}");
    }
}
