using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Communications.Files;

/// <summary> Хранит защищённые вложения отдельно от очередей и выдаёт содержимое после проверки доступа. </summary>
internal sealed class ConversationAttachmentService(
    OpenCRMContext context,
    IOrganizationConversationService conversations,
    IFileStorageService storage,
    IDataProtectionProvider protection,
    IOrganizationSubscriptionService subscriptions,
    ILogger<ConversationAttachmentService> logger) : IConversationAttachmentService
{
    private const int MaximumSize = 20 * 1024 * 1024;
    private readonly OpenCRMContext _context = context;
    private readonly IOrganizationConversationService _conversations = conversations;
    private readonly IFileStorageService _storage = storage;
    private readonly IDataProtectionProvider _protection = protection;
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;
    private readonly ILogger<ConversationAttachmentService> _logger = logger;

    public async Task<ConversationAttachmentDto> UploadAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MessagesSend);
        var page = await this._conversations.GetMessagesAsync(userId, organizationId, conversationId, pageSize: 1, cancellationToken: cancellationToken)
            ?? throw new KeyNotFoundException();
        if (!page.CanSend || page.IsReadOnly)
        {
            throw new UnauthorizedAccessException();
        }

        if (sizeBytes is <= 0 or > MaximumSize)
        {
            throw new ArgumentException("Выберите непустой файл размером до 20 МБ.");
        }

        var safeName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 200 || safeName.Any(char.IsControl))
        {
            throw new ArgumentException("Укажите корректное имя файла длиной до 200 символов.");
        }

        var bytes = new byte[checked((int)sizeBytes)];
        await content.ReadExactlyAsync(bytes, cancellationToken);
        var extra = new byte[1];
        if (await content.ReadAsync(extra, cancellationToken) != 0)
        {
            throw new ArgumentException("Размер файла не соответствует запросу.");
        }

        var imageType = DetectImageType(bytes);
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && imageType is null)
        {
            throw new ArgumentException("Поддерживаются фотографии JPEG, PNG и WebP.");
        }

        if (imageType is not null && sizeBytes > 10 * 1024 * 1024)
        {
            throw new ArgumentException("Размер фотографии не должен превышать 10 МБ.");
        }

        var attachment = new OrganizationConversationAttachment
        {
            OrganizationId = organizationId,
            ConversationId = conversationId,
            UploadedByUserId = userId,
            FileName = safeName,
            ContentType = imageType ?? "application/octet-stream",
            Kind = imageType is null ? "document" : "photo",
            SizeBytes = sizeBytes
        };

        var key = $"private/conversations/{organizationId:N}/{conversationId:N}/{attachment.Id:N}.protected";
        // Общий бакет может содержать публичные медиа: переписка хранится только в защищённом виде.
        var protectedBytes = this.CreateProtector(attachment).Protect(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        await using var stream = new MemoryStream(protectedBytes, writable: false);
        attachment.StorageReference = await this._storage.SaveFileAsync(key, stream);
        try
        {
            await this._context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await this._context.Database.BeginTransactionAsync(cancellationToken);
                var quotaKey = $"conversation-drafts:{organizationId:N}:{userId}";
                await this._context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({quotaKey}, 0))", cancellationToken);
                if (await this._context.OrganizationConversationAttachments.AsNoTracking().AnyAsync(item => item.Id == attachment.Id, cancellationToken))
                {
                    return;
                }

                var pendingCount = await this._context.OrganizationConversationAttachments.CountAsync(item =>
                    item.OrganizationId == organizationId && item.UploadedByUserId == userId && item.MessageId == null, cancellationToken);
                if (pendingCount >= 5)
                {
                    throw new ArgumentException("У вас уже загружено пять неотправленных файлов. Отправьте их или подождите удаления временных файлов через сутки.");
                }

                this._context.OrganizationConversationAttachments.Add(attachment);
                await this._context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch
        {
            try
            {
                if (!await this._context.OrganizationConversationAttachments.AsNoTracking().AnyAsync(item => item.Id == attachment.Id, CancellationToken.None))
                {
                    await this._storage.DeleteFileAsync(attachment.StorageReference);
                }
            }
            catch (Exception exception)
            {
                this._logger.LogWarning(exception, "Не удалось удалить незарегистрированное временное вложение {AttachmentId}.", attachment.Id);
            }

            throw;
        }

        return new ConversationAttachmentDto
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            Kind = attachment.Kind,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes
        };
    }

    public async Task<ConversationAttachmentContentDto?> GetAsync(
        string userId,
        Guid organizationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await this._context.OrganizationConversationAttachments.AsNoTracking()
            .Include(item => item.Conversation)
            .ThenInclude(item => item.Connection)
            .SingleOrDefaultAsync(item => item.Id == attachmentId && item.OrganizationId == organizationId, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var page = await this._conversations.GetMessagesAsync(userId, organizationId, attachment.ConversationId, pageSize: 1, cancellationToken: cancellationToken);
        if (page is null)
        {
            return null;
        }

        if (attachment.MessageId is null && attachment.UploadedByUserId != userId)
        {
            throw new UnauthorizedAccessException();
        }

        return await this.ReadAsync(attachment, cancellationToken);
    }

    public async Task<ConversationAttachmentContentDto?> GetForDeliveryAsync(
        Guid messageId,
        Guid leaseId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await this._context.OrganizationConversationAttachments.AsNoTracking()
            .Include(item => item.Conversation)
            .ThenInclude(item => item.Connection)
            .SingleOrDefaultAsync(item => item.Id == attachmentId && item.MessageId == messageId
                && item.Message != null && item.Message.Status == "sending"
                && item.Message.LeaseId == leaseId && item.Message.LeaseExpiresAt > DateTime.UtcNow, cancellationToken);

        return attachment is null ? null : await this.ReadAsync(attachment, cancellationToken);
    }

    private async Task<ConversationAttachmentContentDto> ReadAsync(OrganizationConversationAttachment attachment, CancellationToken cancellationToken)
    {
        byte[]? content = null;
        if (attachment.StorageReference is not null)
        {
            await using var stream = await this._storage.GetFileAsync(attachment.StorageReference);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaximumSize + 4096)
                {
                    throw new InvalidOperationException("Размер сохранённого вложения превышает допустимый.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            content = this.CreateProtector(attachment).Unprotect(buffer.ToArray());
            if (content.LongLength != attachment.SizeBytes)
            {
                throw new InvalidOperationException("Размер вложения не соответствует сохранённым данным.");
            }
        }

        return new ConversationAttachmentContentDto
        {
            OrganizationId = attachment.OrganizationId,
            BotId = attachment.Conversation.BotId,
            ConnectionId = attachment.Conversation.ConnectionId,
            ProviderCode = attachment.Conversation.Connection?.ProviderCode ?? "telegram",
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            ProviderFileId = attachment.ProviderFileId,
            Content = content
        };
    }

    private IDataProtector CreateProtector(OrganizationConversationAttachment attachment) =>
        this._protection.CreateProtector("OpenCRM.ConversationAttachment.v1", $"{attachment.OrganizationId:N}:{attachment.ConversationId:N}:{attachment.Id:N}");

    private static string? DetectImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            ? "image/webp"
            : null;
    }
}
