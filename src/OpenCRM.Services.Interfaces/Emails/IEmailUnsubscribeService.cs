using OpenCRM.Services.Interfaces.Emails.Dtos;
namespace OpenCRM.Services.Interfaces.Emails;

/// <summary> Отписка пользователей от email-рассылок. </summary>
public interface IEmailUnsubscribeService
{
    /// <summary> Отписывает пользователя по ссылке с токеном. </summary>
    Task<EmailUnsubscribeResult> UnsubscribeAsync(
        string userId,
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
