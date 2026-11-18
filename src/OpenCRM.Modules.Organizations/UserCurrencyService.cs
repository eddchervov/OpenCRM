using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Управляет личной валютой пользователя. </summary>
internal sealed class UserCurrencyService(OpenCRMContext context) : IUserCurrencyService
{
    public async Task<CurrencySettingsDto> GetAsync(string userId)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId)
            ?? throw new KeyNotFoundException("Пользователь не найден.");
        var canChange = !await context.Vehicles.IgnoreQueryFilters()
            .AnyAsync(vehicle => vehicle.UserId == userId && vehicle.OrganizationId == null)
            && !await context.Maintenance.IgnoreQueryFilters().AnyAsync(maintenance =>
                maintenance.UserId == userId && maintenance.Vehicle.OrganizationId == null);

        return new CurrencySettingsDto
        {
            CurrencyCode = user.PersonalCurrencyCode,
            CanChange = canChange,
            CanManage = true
        };
    }

    public async Task<CurrencySettingsDto> UpdateAsync(string userId, UpdateCurrencySettingsDto dto)
    {
        var currencyCode = dto.CurrencyCode?.Trim().ToUpperInvariant();
        if (!CurrencyCodes.IsSupported(currencyCode))
        {
            throw new InvalidOperationException("Выберите поддерживаемую валюту.");
        }

        var isRetry = false;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM identity."AspNetUsers" WHERE "Id" = {userId} FOR NO KEY UPDATE""");
            var user = await context.Users.SingleOrDefaultAsync(item => item.Id == userId)
                ?? throw new KeyNotFoundException("Пользователь не найден.");
            if (!string.Equals(user.PersonalCurrencyCode, dto.ExpectedCurrencyCode, StringComparison.Ordinal))
            {
                throw new DbUpdateConcurrencyException("Личная валюта уже изменена. Обновите настройки и повторите попытку.");
            }

            if (user.PersonalCurrencyCode != currencyCode
                && (await context.Vehicles.IgnoreQueryFilters()
                    .AnyAsync(vehicle => vehicle.UserId == userId && vehicle.OrganizationId == null)
                    || await context.Maintenance.IgnoreQueryFilters().AnyAsync(maintenance =>
                        maintenance.UserId == userId && maintenance.Vehicle.OrganizationId == null)))
            {
                throw new InvalidOperationException("Личную валюту нельзя изменить после создания личных данных.");
            }

            user.PersonalCurrencyCode = currencyCode!;
            await context.SaveChangesAsync();
        }, () => context.Users.AsNoTracking().AnyAsync(user => user.Id == userId
            && user.PersonalCurrencyCode == currencyCode));

        return await this.GetAsync(userId);
    }
}
