using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    public Task<IReadOnlyList<AdminCrmImportUser>> GetCrmImportUsersAsync(string search, CancellationToken cancellationToken) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminCrmImportUser>>(
            $"api/v1/admin/crm-import/users?search={Uri.EscapeDataString(search)}", "Не удалось найти пользователей.", cancellationToken);

    public Task<IReadOnlyList<AdminCrmImportOrganization>> GetCrmImportOrganizationsAsync(string userId, CancellationToken cancellationToken) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminCrmImportOrganization>>(
            $"api/v1/admin/crm-import/users/{Uri.EscapeDataString(userId)}/organizations", "Не удалось загрузить организации.", cancellationToken);

    public Task<AdminCrmImportPreview> CreateCrmImportPreviewAsync(
        string userId, Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length is 0 or > 10 * 1024 * 1024)
        {
            throw new ArgumentException("Выберите файл размером от 1 байта до 10 МБ.", nameof(content));
        }

        return this.SendAuthorizedContentAsync<AdminCrmImportPreview>(HttpMethod.Post, "api/v1/admin/crm-import/previews", () =>
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(userId), "userId");
            form.Add(new StringContent(organizationId.ToString()), "organizationId");
            form.Add(new ByteArrayContent(content), "file", Path.GetFileName(fileName));

            return form;
        }, "Не удалось проверить файл импорта.", cancellationToken);
    }

    public Task<AdminCrmImportPreview> GetCrmImportPreviewAsync(Guid id, CancellationToken cancellationToken) =>
        this.GetAuthorizedReportAsync<AdminCrmImportPreview>($"api/v1/admin/crm-import/previews/{id}",
            "Не удалось обновить предпросмотр.", cancellationToken);

    public Task<AdminCrmImportResult> ConfirmCrmImportAsync(Guid id, CancellationToken cancellationToken) =>
        this.SendAuthorizedRequestAsync<AdminCrmImportResult>(HttpMethod.Post,
            $"api/v1/admin/crm-import/previews/{id}/confirm", null, "Не удалось подтвердить импорт.", cancellationToken);

    public Task<byte[]> DownloadCrmImportReportAsync(Guid id, bool result, CancellationToken cancellationToken) =>
        this.GetAuthorizedReportAsync<byte[]>($"api/v1/admin/crm-import/previews/{id}/{(result ? "result-report" : "report")}",
            "Не удалось скачать отчёт.", cancellationToken);
}
