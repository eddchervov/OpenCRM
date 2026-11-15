using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Users;

namespace OpenCRM.Modules.Accounts.Users;

/// <summary> Сервис удаления аккаунтов пользователей. </summary>
public sealed class AccountDeletionService(
    UserManager<ApplicationUser> userManager,
    IApplicationUnitOfWork unitOfWork,
    OpenCRMContext context) : IAccountDeletionService
{
    /// <summary> Удаляет аккаунт пользователя и возвращает сообщение об ошибке при неудаче. </summary>
    public async Task<string?> DeleteAsync(ApplicationUser user)
    {
        var originalEmail = user.OriginalEmail;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (string.IsNullOrEmpty(user.OriginalEmail))
            {
                user.OriginalEmail = user.Email;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var deletedEmail = $"deleted_{timestamp}_{user.Id}@deleted.local";

            await userManager.SetEmailAsync(user, deletedEmail);
            await userManager.SetUserNameAsync(user, deletedEmail);

            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;

            var externalLogins = await userManager.GetLoginsAsync(user);
            foreach (var login in externalLogins)
            {
                await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            }

            await userManager.UpdateAsync(user);
            originalEmail = user.OriginalEmail;
            TelegramAdminNotificationPublisher.AddDeletion(context, user);
            await context.SaveChangesAsync();
        });

        return originalEmail;
    }
}
