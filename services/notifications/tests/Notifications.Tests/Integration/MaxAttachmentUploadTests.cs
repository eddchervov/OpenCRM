using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Contracts;
using Notifications.Infrastructure;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет загрузку MAX-вложений, когда POST /uploads возвращает только URL.</summary>
[Trait("Category", "Integration")]
public sealed class MaxAttachmentUploadTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    [Theory]
    [InlineData("image", "{\"photos\":{\"photo-1\":{\"token\":\"image-token\"}}}", "image-token")]
    [InlineData("file", "{\"fileId\":123,\"token\":\"file-token\"}", "file-token")]
    public async Task UrlOnlyUploadDescriptorUsesTokenFromUploadResponse(
        string kind,
        string uploadResult,
        string expectedToken)
    {
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var uploadedMessage = false;
        var completed = false;
        string? sentAttachmentToken = null;
        var attachmentBytes = new byte[] { 10, 20, 30 };
        var coreHandler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal))
            {
                var claim = new ConversationDeliveryClaimResponse
                {
                    MessageId = messageId,
                    LeaseId = leaseId,
                    OrganizationId = Guid.NewGuid(),
                    ProviderCode = ConversationDeliveryProvider.Max,
                    ConnectionId = Guid.NewGuid(),
                    ProviderBotId = "123",
                    DeliveryAddress = "456",
                    ExternalConversationId = "456",
                    BotUsername ="test_bot",
                    RequestId = requestId,
                    Token = "max-token",
                    Text = "Вложение",
                    Attachments =
                    [
                        new ConversationDeliveryAttachment
                        {
                            AttachmentId = attachmentId,
                            Kind = kind,
                            FileName = kind == "image" ? "photo.jpg" : "document.pdf",
                            ContentType = kind == "image" ? "image/jpeg" : "application/pdf",
                            SizeBytes = attachmentBytes.Length
                        }
                    ]
                };

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(claim) };
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/attachments/{attachmentId:D}", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(attachmentBytes)
                };
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal))
            {
                completed = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected core request {request.RequestUri}.");
        });
        var maxApiHandler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/uploads")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"url\":\"https://fu.oneme.ru/api/upload.do?signature=opaque\"}")
                };
            }

            if (request.RequestUri.AbsolutePath == "/messages")
            {
                uploadedMessage = true;
                using var body = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync());
                sentAttachmentToken = body.RootElement.GetProperty("attachments")[0]
                    .GetProperty("payload").GetProperty("token").GetString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"message\":{\"body\":{\"mid\":\"max-mid\"}}}")
                };
            }

            throw new InvalidOperationException($"Unexpected MAX API request {request.RequestUri}.");
        });
        var uploadHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(uploadResult)
        }));
        var clients = new UploadTestHttpClientFactory(coreHandler, maxApiHandler, uploadHandler);
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var processor = new MaxChatDeliveryProcessor(
            clients,
            new ConversationDeliveryAttemptStore(context),
            new MaxConversationRateLimiter(context, TimeProvider.System),
            Options.Create(new MaxChatOptions
            {
                OpenCRMBaseUrl = "https://opencrm.example",
                ServiceKey = new string('s', 40),
                RequestTimeoutSeconds = 5
            }));

        await processor.ProcessAsync(
            new ConversationDeliveryWakeup(ConversationDeliveryProvider.Max, messageId, requestId),
            CancellationToken.None);

        Assert.True(uploadedMessage);
        Assert.True(completed);
        Assert.Equal(expectedToken, sentAttachmentToken);
    }

    [Fact]
    public async Task FailedUploadRemainsSafeToRetryWithoutCallingMessageEndpoint()
    {
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var sentMessage = false;
        JsonDocument? completedResult = null;
        var attachmentBytes = new byte[] { 10, 20, 30 };
        var coreHandler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal))
            {
                var claim = new ConversationDeliveryClaimResponse
                {
                    MessageId = messageId,
                    LeaseId = leaseId,
                    OrganizationId = Guid.NewGuid(),
                    ProviderCode = ConversationDeliveryProvider.Max,
                    ConnectionId = Guid.NewGuid(),
                    ProviderBotId = "123",
                    DeliveryAddress = "456",
                    ExternalConversationId = "456",
                    BotUsername ="test_bot",
                    RequestId = requestId,
                    Token = "max-token",
                    Text = "Вложение",
                    Attachments =
                    [
                        new ConversationDeliveryAttachment
                        {
                            AttachmentId = attachmentId,
                            Kind = "file",
                            FileName = "document.pdf",
                            ContentType = "application/pdf",
                            SizeBytes = attachmentBytes.Length
                        }
                    ]
                };

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(claim) };
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/attachments/{attachmentId:D}", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(attachmentBytes)
                };
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal))
            {
                completedResult = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected core request {request.RequestUri}.");
        });
        var maxApiHandler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/messages")
            {
                sentMessage = true;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var uploadHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var clients = new UploadTestHttpClientFactory(coreHandler, maxApiHandler, uploadHandler);
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var processor = new MaxChatDeliveryProcessor(
            clients,
            new ConversationDeliveryAttemptStore(context),
            new MaxConversationRateLimiter(context, TimeProvider.System),
            Options.Create(new MaxChatOptions
            {
                OpenCRMBaseUrl = "https://opencrm.example",
                ServiceKey = new string('s', 40),
                RequestTimeoutSeconds = 5
            }));

        await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => processor.ProcessAsync(
            new ConversationDeliveryWakeup(ConversationDeliveryProvider.Max, messageId, requestId),
            CancellationToken.None));

        Assert.False(sentMessage);
        Assert.NotNull(completedResult);
        var result = completedResult.RootElement.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("safeToRetry").GetBoolean());
        Assert.Equal("attachment-upload-failed", result.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task OversizedCoreAttachmentIsRejectedBeforeUpload()
    {
        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var uploadCalled = false;
        JsonDocument? completedResult = null;
        var coreHandler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal))
            {
                var claim = new ConversationDeliveryClaimResponse
                {
                    MessageId = messageId,
                    LeaseId = leaseId,
                    OrganizationId = Guid.NewGuid(),
                    ProviderCode = ConversationDeliveryProvider.Max,
                    ConnectionId = Guid.NewGuid(),
                    ProviderBotId = "123",
                    DeliveryAddress = "456",
                    ExternalConversationId = "456",
                    BotUsername ="test_bot",
                    RequestId = requestId,
                    Token = "max-token",
                    Text = "Вложение",
                    Attachments =
                    [
                        new ConversationDeliveryAttachment
                        {
                            AttachmentId = attachmentId,
                            Kind = "file",
                            FileName = "document.pdf",
                            ContentType = "application/pdf",
                            SizeBytes = 3
                        }
                    ]
                };

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(claim) };
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/attachments/{attachmentId:D}", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream([10, 20, 30, 40]))
                };
                response.Content.Headers.ContentLength = 3;

                return response;
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal))
            {
                completedResult = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected core request {request.RequestUri}.");
        });
        var maxApiHandler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/uploads")
            {
                uploadCalled = true;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":{\"body\":{\"mid\":\"max-mid\"}}}")
            });
        });
        var clients = new UploadTestHttpClientFactory(coreHandler, maxApiHandler,
            new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var processor = new MaxChatDeliveryProcessor(
            clients,
            new ConversationDeliveryAttemptStore(context),
            new MaxConversationRateLimiter(context, TimeProvider.System),
            Options.Create(new MaxChatOptions
            {
                OpenCRMBaseUrl = "https://opencrm.example",
                ServiceKey = new string('s', 40),
                RequestTimeoutSeconds = 5
            }));

        await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => processor.ProcessAsync(
            new ConversationDeliveryWakeup(ConversationDeliveryProvider.Max, messageId, requestId),
            CancellationToken.None));

        Assert.False(uploadCalled);
        Assert.NotNull(completedResult);
        var result = completedResult.RootElement.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("safeToRetry").GetBoolean());
        Assert.Equal("attachment-unavailable", result.GetProperty("errorCode").GetString());
    }

    private sealed class UploadTestHttpClientFactory(
        HttpMessageHandler coreHandler,
        HttpMessageHandler maxApiHandler,
        HttpMessageHandler uploadHandler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = name switch
            {
                "MaxChatOpenCRM" => coreHandler,
                "MaxConversationApi" => maxApiHandler,
                "MaxConversationUpload" => uploadHandler,
                _ => throw new InvalidOperationException($"Unexpected HTTP client: {name}.")
            };
            var baseAddress = name switch
            {
                "MaxConversationApi" => new Uri("https://platform-api2.max.ru/"),
                "MaxConversationUpload" => new Uri("https://fu.oneme.ru/"),
                _ => new Uri("https://opencrm.example/")
            };

            return new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress };
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
