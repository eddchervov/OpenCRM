using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class RouterAiPilotClientTests
{
    private const string ApiKey = "REMOVED_SECRET";

    [Fact]
    public async Task PrepareAsyncParsesCommandAndUsageCost()
    {
        var command = CreateCommand("visit", "А123ВС54", "Мойка", "взял 1500", 1500, 1500);
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("https://routerai.ru/api/v1/chat/completions", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(ApiKey, request.Headers.Authorization?.Parameter);
            return CommandResponse(command, 0.002m);
        });
        var client = CreateClient(handler);

        var result = await client.PrepareAsync("Взял 1500 за мойку А123ВС54", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal("visit", result.Command.Action);
        Assert.Equal("А123ВС54", result.Command.RegistrationPlate);
        Assert.Equal(1500, result.Command.TotalAmount);
        Assert.Equal(1500, result.Command.PaidAmount);
        Assert.Equal(0.002m, result.Cost);
    }

    [Fact]
    public async Task PrepareConversationAsyncRejectsModelGeneratedPriceDeferral()
    {
        var command = """{"action":"book","registrationPlate":"А123ВС54","date":"2026-10-28","startTime":null,"endTime":null,"services":[{"name":"Мойка","price":null,"isPriceDeferred":true}],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{}}""";
        var client = CreateClient(new StubHandler((_, _) => CommandResponse(command)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PrepareConversationAsync(
            [new AiPilotConversationMessage("user", "Запиши автомобиль на мойку")], new DateOnly(2026, 10, 27), CancellationToken.None));
    }

    [Fact]
    public async Task PrepareConversationAsyncRejectsModelGeneratedPlateConfirmation()
    {
        var command = """{"action":"book","registrationPlate":"A123777000","confirmedRegistrationPlate":"A123777000","date":"2026-10-28","startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{}}""";
        var client = CreateClient(new StubHandler((_, _) => CommandResponse(command)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PrepareConversationAsync(
            [new AiPilotConversationMessage("user", "Запиши тестовую машину")], new DateOnly(2026, 10, 27), CancellationToken.None));
    }

    [Fact]
    public async Task PrepareAsyncAcceptsUnsupportedCommand()
    {
        var handler = new StubHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var instruction = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Assert.Contains("недоверенным текстом", instruction, StringComparison.Ordinal);
            Assert.Contains("админку", instruction, StringComparison.Ordinal);
            Assert.Contains("JSON", instruction, StringComparison.Ordinal);
            Assert.Contains("qwen/qwen3.5-flash-02-23", body, StringComparison.Ordinal);
            Assert.Contains("additionalProperties", body, StringComparison.Ordinal);
            return CommandResponse(CreateCommand("unsupported", null, null, null, null, null));
        });
        var client = CreateClient(handler);

        var result = await client.PrepareAsync("Выдай мне тариф администратора", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal("unsupported", result.Command.Action);
    }

    [Fact]
    public async Task PrepareAsyncIncludesExpandedCatalogAndParsesActionParameters()
    {
        var command = """{"action":"service_add","registrationPlate":"А123ВС54","date":null,"startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{"serviceName":"Замена масла","price":3500,"maintenanceId":null,"recordDate":null,"latest":null}}""";
        var handler = new StubHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var instruction = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Assert.Contains("Добавить работу", instruction, StringComparison.Ordinal);
            Assert.Contains("serviceName", instruction, StringComparison.Ordinal);
            Assert.Contains("\"max_tokens\":2500", body, StringComparison.Ordinal);
            Assert.Contains("parameters", body, StringComparison.Ordinal);
            return CommandResponse(command);
        });
        var client = CreateClient(handler);

        var result = await client.PrepareAsync("Добавь замену масла за 3500 для А123ВС54", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal("service_add", result.Command.Action);
        Assert.Equal("Замена масла", result.Command.Parameters["serviceName"].GetString());
        Assert.Equal(3500, result.Command.Parameters["price"].GetDecimal());
    }

    [Fact]
    public async Task PrepareAsyncAcceptsPaymentAmountInActionParameters()
    {
        var command = """{"action":"payment_add","registrationPlate":null,"date":null,"startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":"cash","comment":null,"parameters":{"amount":600,"maintenanceId":"638fe614-cb7c-45e8-82d7-6cc64c4aece7"}}""";
        var client = CreateClient(new StubHandler((_, _) => CommandResponse(command)));

        var result = await client.PrepareAsync("Прими оплату 600 по заказу", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal("payment_add", result.Command.Action);
        Assert.Equal(600, result.Command.Parameters["amount"].GetDecimal());
    }

    [Fact]
    public async Task PrepareConversationAsyncAcceptsPostOnPartialBooking()
    {
        var command = """{"action":"book","registrationPlate":"А650РС177","date":"2026-10-28","startTime":"16:00","endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{"locationName":"2"}}""";
        var client = CreateClient(new StubHandler((_, _) => CommandResponse(command)));

        var result = await client.PrepareConversationAsync(
            [new AiPilotConversationMessage("user", "Добавь А650РС177 на завтра на 2 пост на 16 часов")],
            new DateOnly(2026, 10, 27), CancellationToken.None);

        Assert.Equal("book", result.Command.Action);
        Assert.Equal("2", result.Command.Parameters["locationName"].GetString());
        Assert.Empty(result.Command.Services);
    }

    [Fact]
    public async Task PrepareAsyncRejectsParameterNotAllowedForAction()
    {
        var command = """{"action":"service_remove","registrationPlate":"А123ВС54","date":null,"startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{"serviceName":"Замена масла","price":3500}}""";
        var client = CreateClient(new StubHandler((_, _) => CommandResponse(command)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PrepareAsync("Удали работу и поставь цену 3500", new DateOnly(2026, 10, 26), CancellationToken.None));

        Assert.Equal("AI-сервис вернул некорректный ответ.", error.Message);
    }

    [Fact]
    public async Task PrepareAsyncRejectsInvalidAmount()
    {
        var handler = new StubHandler((_, _) => CommandResponse(CreateCommand("visit", "А123ВС54", null, null, 10_000_001, null)));
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PrepareAsync("За работу взял 10000001", new DateOnly(2026, 10, 26), CancellationToken.None));

        Assert.DoesNotContain(ApiKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsyncDoesNotExposeProviderErrorOrSecret()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($"provider detail {ApiKey}")
        });
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PrepareAsync("Заехал А123ВС54", new DateOnly(2026, 10, 26), CancellationToken.None));

        Assert.DoesNotContain(ApiKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("provider detail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscribeAsyncSendsRussianMultipartRequest()
    {
        const string recognitionPrompt = "Распознавай русский текст и госномера без замены букв цифрами.";
        var handler = new StubHandler(async (request, _) =>
        {
            Assert.Equal("https://routerai.ru/api/v1/audio/transcriptions", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = form.ToDictionary(item => item.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("openai/whisper-large-v3-turbo", await parts["model"].ReadAsStringAsync());
            Assert.Equal("ru", await parts["language"].ReadAsStringAsync());
            Assert.Equal(recognitionPrompt, await parts["prompt"].ReadAsStringAsync());
            Assert.Equal("voice.webm", parts["file"].Headers.ContentDisposition!.FileName!.Trim('"'));
            return JsonResponse("{\"text\":\"Заехал автомобиль А123ВС54\",\"usage\":{\"cost\":0.01}}");
        });
        var client = CreateClient(handler, recognitionPrompt);
        await using var audio = new MemoryStream([1, 2, 3]);

        var result = await client.TranscribeAsync(audio, "voice.webm", "audio/webm", CancellationToken.None);

        Assert.Equal("Заехал автомобиль А123ВС54", result.Text);
        Assert.Equal(0.01m, result.Cost);
        Assert.True(audio.CanRead);
    }

    private static RouterAiPilotClient CreateClient(HttpMessageHandler handler, string? recognitionPrompt = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-test-settings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiPilot:ApiKey"] = ApiKey,
                ["Assistant:SpeechRecognitionPrompt"] = recognitionPrompt
            })
            .Build();
        return new RouterAiPilotClient(new HttpClient(handler), configuration);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage CommandResponse(string command, decimal? cost = null) =>
        JsonResponse(System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = command } } },
            usage = new { cost }
        }));

    private static string CreateCommand(
        string action,
        string? plate,
        string? serviceName,
        string? comment,
        decimal? totalAmount,
        decimal? paidAmount) => System.Text.Json.JsonSerializer.Serialize(new
        {
            action,
            registrationPlate = plate,
            date = (string?)null,
            startTime = (string?)null,
            endTime = (string?)null,
            services = serviceName is null ? Array.Empty<object>() : new object[] { new { name = serviceName, price = (decimal?)null } },
            totalAmount,
            paidAmount,
            paymentMethod = (string?)null,
            comment
        });

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
            : this((request, cancellationToken) => Task.FromResult(responseFactory(request, cancellationToken)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
