using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Modules.Support.Personal;

/// <summary> Хранит вложения в защищённом виде и выдаёт их после проверки доступа. </summary>
public sealed class SupportAttachmentService(
    OpenCRMContext context,
    IFileStorageService storage,
    IDataProtectionProvider protection,
    ILogger<SupportAttachmentService> logger) : ISupportAttachmentService
{
    internal async Task<List<SupportAttachment>> SaveAsync(IReadOnlyList<SupportAttachmentUploadDto> uploads)
    {
        SupportAttachmentRules.Validate(uploads);
        var attachments = new List<SupportAttachment>();
        try
        {
            foreach (var upload in uploads)
            {
                var attachment = new SupportAttachment
                {
                    FileName = SupportAttachmentRules.NormalizeFileName(upload.FileName),
                    SizeBytes = upload.Content.LongLength,
                    SortOrder = attachments.Count
                };

                // В общем бакете могут быть публичные файлы, поэтому сохраняем только шифротекст.
                var bytes = this.CreateProtector(attachment.Id).Protect(upload.Content);
                await using var stream = new MemoryStream(bytes, writable: false);
                attachment.StorageReference = $"private/support/{attachment.Id:N}.protected";
                attachments.Add(attachment);
                attachment.StorageReference = await storage.SaveFileAsync(attachment.StorageReference, stream);
            }

            return attachments;
        }
        catch
        {
            await this.CleanupAsync(attachments);

            throw;
        }
    }

    internal async Task CleanupAsync(IReadOnlyList<SupportAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            try
            {
                // При неизвестном результате commit не удаляем файл уже сохранённого обращения.
                if (!await context.SupportAttachments.AsNoTracking().AnyAsync(x => x.Id == attachment.Id))
                {
                    await storage.DeleteFileAsync(attachment.StorageReference);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Не удалось очистить незарегистрированное вложение поддержки {AttachmentId}.", attachment.Id);
            }
        }
    }

    public async Task<SupportAttachmentContentDto?> GetForUserAsync(
        string userId, Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await context.SupportAttachments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.SupportMessage.UserId == userId, cancellationToken);

        return attachment == null ? null : await this.ReadAsync(attachment, cancellationToken);
    }

    public async Task<SupportAttachmentContentDto?> GetForAdminAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await context.SupportAttachments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return attachment == null ? null : await this.ReadAsync(attachment, cancellationToken);
    }

    private async Task<SupportAttachmentContentDto> ReadAsync(SupportAttachment attachment, CancellationToken cancellationToken)
    {
        await using var stream = await storage.GetFileAsync(attachment.StorageReference);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > SupportAttachmentRules.MaximumFileSize + 4096)
            {
                throw new InvalidOperationException("Сохранённое вложение превышает допустимый размер.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var bytes = this.CreateProtector(attachment.Id).Unprotect(buffer.ToArray());
        if (bytes.LongLength != attachment.SizeBytes)
        {
            throw new InvalidOperationException("Размер вложения не соответствует сохранённым данным.");
        }

        return new SupportAttachmentContentDto { FileName = attachment.FileName, Content = bytes };
    }

    private IDataProtector CreateProtector(Guid id) =>
        protection.CreateProtector("OpenCRM.SupportAttachment.v1", id.ToString("N"));
}
