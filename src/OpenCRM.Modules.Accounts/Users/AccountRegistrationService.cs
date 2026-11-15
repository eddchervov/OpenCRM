using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Users;

/// <summary> Сервис регистрации аккаунтов пользователей. </summary>
public sealed class AccountRegistrationService(
    UserManager<ApplicationUser> userManager,
    IApplicationUnitOfWork unitOfWork,
    ISiteNotificationPublisher siteNotificationPublisher,
    OpenCRMContext context) : IAccountRegistrationService
{
    /// <summary> Создаёт аккаунт пользователя по запросу регистрации. </summary>
    public async Task<AccountCreationResult> CreateAsync(AccountRegistrationRequest request)
    {
        ApplicationUser? user = null;
        IdentityResult result = IdentityResult.Success;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            user = new ApplicationUser
            {
                Email = request.Email,
                UserName = request.Email,
                OriginalEmail = request.Email,
                DatetimeCreation = DateTime.UtcNow,
                EmailConfirmed = request.Password == null
            };

            request.Attribution?.ApplyTo(user);
            var acceptedAt = DateTime.UtcNow;
            user.PersonalDataProcessingConsentAccepted = request.PersonalDataProcessingConsent;
            user.PersonalDataProcessingConsentAcceptedAt = request.PersonalDataProcessingConsent ? acceptedAt : null;
            user.AdvertisingConsentAccepted = request.AdvertisingConsent;
            user.AdvertisingConsentAcceptedAt = request.AdvertisingConsent ? acceptedAt : null;
            result = request.Password == null
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return;
            }

            await siteNotificationPublisher.AddRegistrationNotificationsAsync(user);
            TelegramAdminNotificationPublisher.AddRegistration(context, user);
            await context.SaveChangesAsync();
        });

        return new AccountCreationResult(
            result.Succeeded ? user : null,
            result.Errors.Select(error => error.Description).ToArray());
    }
}
