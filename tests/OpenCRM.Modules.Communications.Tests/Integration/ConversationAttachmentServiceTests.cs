using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;
using Moq;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[Collection(ConversationDatabaseCollection.Name)]
public sealed class ConversationAttachmentServiceTests(ConversationDatabaseFixture fixture)
{
    [Fact]
    public async Task UploadedAttachmentIsEncryptedAndPrivateToUploaderAndTenant()
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, employee) = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другая студия");
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 12011));
        await context.SaveChangesAsync();
        var storedFiles = new ConcurrentDictionary<string, byte[]>();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((string key, Stream content) => SaveAsync(key, content, storedFiles));
        storage.Setup(item => item.GetFileAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => new MemoryStream(storedFiles[key], writable: false));

        using var services = fixture.CreateServices(context, fileStorage: storage);
        var conversationService = services.GetRequiredService<IOrganizationConversationService>();
        await conversationService.IngestTelegramMessageAsync(CreateUpdate(organization.Id));
        var conversation = (await conversationService.GetInboxAsync(owner.Id, organization.Id, new ConversationInboxQueryDto())).Items.Single();
        var attachments = services.GetRequiredService<IConversationAttachmentService>();
        var original = new byte[] { 65, 66, 67, 68 };
        var attachment = await attachments.UploadAsync(owner.Id, organization.Id, conversation.Id,
            "notes.txt", "text/plain", original.Length, new MemoryStream(original));
        var encrypted = Assert.Single(storedFiles).Value;

        Assert.NotEqual(original, encrypted);
        var ownerContent = await attachments.GetAsync(owner.Id, organization.Id, attachment.Id);
        Assert.Equal(original, ownerContent!.Content);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attachments.GetAsync(employee.Id, organization.Id, attachment.Id));
        Assert.Null(await attachments.GetAsync(otherOrganization.Owner.Id, otherOrganization.Organization.Id, attachment.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => attachments.UploadAsync(owner.Id, organization.Id, conversation.Id,
            "too-large.bin", "application/octet-stream", 20 * 1024 * 1024 + 1, Stream.Null));
        Assert.Single(storedFiles);
    }

    private static async Task<string> SaveAsync(string key, Stream content, ConcurrentDictionary<string, byte[]> storedFiles)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        storedFiles[key] = buffer.ToArray();

        return key;
    }

    private static IngestTelegramMessageDto CreateUpdate(Guid organizationId) => new()
    {
        OrganizationId = organizationId,
        BotId = 12011,
        BotUsername ="studio_12011_bot",
        UpdateId = 13011,
        ChatId = 88011,
        SenderTelegramUserId = 88011,
        ProviderMessageId = 3011,
        Text = "Проверка вложения",
        SentAt = DateTime.UtcNow
    };
}
