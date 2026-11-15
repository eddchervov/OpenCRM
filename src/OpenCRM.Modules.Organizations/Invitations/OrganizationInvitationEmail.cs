using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using OpenCRM.Services.Interfaces.Emails;

namespace OpenCRM.Modules.Organizations.Invitations;

/// <summary> Отправка email-приглашений в организацию. </summary>
internal sealed class OrganizationInvitationEmail(
    IEmailNotificationService emailNotificationService,
    IConfiguration configuration,
    Helpers.ProjectBrand? brand = null)
{
    /// <summary> Ставит в очередь письмо с приглашением в организацию. </summary>
    internal async Task QueueAsync(string email, string organizationName, Guid invitationId, string invitationToken)
    {
        var baseUrl = (configuration["AppSettings:BaseUrl"] ?? "https://opencrm.example.invalid").TrimEnd('/');
        var invitationUrl = baseUrl + "/auth/emailcodelogin?returnUrl=%2Forganizations";
        var encodedOrganizationName = HtmlEncoder.Default.Encode(organizationName);
        var encodedInvitationUrl = HtmlEncoder.Default.Encode(invitationUrl);
        var html = $"""
            <p style="margin:0 0 16px;font-size:16px;line-height:1.6">
                Вас пригласили в команду организации <strong>{encodedOrganizationName}</strong> в {HtmlEncoder.Default.Encode(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName)}.
            </p>
            <p style="margin:0 0 24px;line-height:1.6">
                Откройте приглашение, укажите этот email и подтвердите вход кодом из письма.
                Если аккаунта ещё нет, он будет создан после подтверждения адреса.
            </p>
            <p style="margin:0 0 24px">
                <a href="{encodedInvitationUrl}" style="display:inline-block;padding:13px 22px;border-radius:8px;background:#202631;color:#ffffff;text-decoration:none;font-weight:600">
                    Открыть приглашение
                </a>
            </p>
            <p style="margin:0 0 8px;color:#687386;font-size:14px;line-height:1.5">
                После входа приглашение появится на странице организаций. Принятие подтвердите там.
            </p>
            <p style="margin:0;color:#687386;font-size:12px;line-height:1.5">
                Если вы не ожидали этого приглашения, просто проигнорируйте письмо.
            </p>
            """;

        await emailNotificationService.QueueAsync(
            email,
            $"Приглашение в организацию {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}",
            html,
            $"organization-invitation:{invitationId:N}:{invitationToken}");
    }
}
