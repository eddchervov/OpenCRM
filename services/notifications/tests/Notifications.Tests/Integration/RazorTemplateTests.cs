using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Domain;
using Notifications.Infrastructure.Templating;
using Razor.Templating.Core;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет скомпилированные Razor-письма, общий макет и строгую модель данных.</summary>
[Trait("Category", "Integration")]
public sealed class RazorTemplateTests
{
    /// <summary> Проверяет отрисовку модели и версионного макета скомпилированного шаблона. </summary>
    [Theory]
    [InlineData(
        "system/email-confirmation",
        """{"name":"Иван","confirmationUrl":"https://example.com/confirm?a=1&b=2"}""",
        "Подтвердите электронную почту",
        "Подтвердить почту")]
    [InlineData(
        "system/password-reset",
        """{"name":"Иван","resetUrl":"https://example.com/reset"}""",
        "Восстановление доступа",
        "Сбросить пароль")]
    [InlineData(
        "system/subscription-expiring",
        """{"name":"Иван","subscriptionName":"Тестовая","endDate":"2026-09-16","renewalUrl":"https://example.com/renew"}""",
        "Подписка заканчивается 16 сентября 2026",
        "Перейти к подписке")]
    public async Task CompiledTemplateRendersModelAndVersionedLayout(
        string key, string json, string expectedSubject, string expectedAction)
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        var content = await renderer.RenderAsync(
            "opencrm", key, NotificationChannel.Email, 1, json, CancellationToken.None);
        var decodedBody = WebUtility.HtmlDecode(content.Body);

        Assert.Equal($"{expectedSubject} — opencrm.example.invalid", content.Subject);
        Assert.True(content.IsHtml);
        Assert.Contains("<!doctype html>", content.Body);
        Assert.Contains("Здравствуйте, Иван!", decodedBody);
        Assert.Contains("opencrm.example.invalid", decodedBody);
        Assert.Contains(expectedAction, decodedBody);
        Assert.DoesNotContain("@Model", content.Body);
        Assert.DoesNotContain("@RenderBody", content.Body);
    }

    /// <summary> Проверяет экранирование пользовательского текста без его исполнения. </summary>
    [Fact]
    public async Task RazorEscapesUserTextAndDoesNotExecuteIt()
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        var content = await renderer.RenderAsync(
            "opencrm", "system/email-confirmation", NotificationChannel.Email, 1,
            """{"name":"<script>@(1+1)</script>","confirmationUrl":"https://example.com/?a=1&b=2"}""",
            CancellationToken.None);

        Assert.Contains("&lt;script&gt;", content.Body);
        Assert.Contains("<script>@(1+1)</script>", WebUtility.HtmlDecode(content.Body));
        Assert.Contains("href=\"https://example.com/?a=1&amp;b=2\"", content.Body);
        Assert.DoesNotContain("<script>", content.Body);
    }

    /// <summary> Проверяет отклонение некорректных или небезопасных данных модели. </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":"Иван"}""")]
    [InlineData("""{"name":null,"confirmationUrl":"https://example.com"}""")]
    [InlineData("""{"name":"  ","confirmationUrl":"https://example.com"}""")]
    [InlineData("""{"name":{"first":"Иван"},"confirmationUrl":"https://example.com"}""")]
    [InlineData("""{"name":"Иван","confirmationUrl":"https://example.com","typo":"value"}""")]
    [InlineData("""{"name":"Иван","confirmationUrl":"javascript:alert(1)"}""")]
    [InlineData("""{"name":"Иван","confirmationUrl":"//example.com"}""")]
    [InlineData("""{"name":"Иван","confirmationUrl":"https://user:contact@example.invalid"}""")]
    [InlineData("""{"name":"Иван","confirmationUrl":"https://example.com/\r\n"}""")]
    public async Task InvalidModelIsRejected(string json)
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            "opencrm", "system/email-confirmation", NotificationChannel.Email, 1, json, CancellationToken.None));
    }

    /// <summary> Проверяет отклонение неизвестного приложения, версии, канала или пути шаблона. </summary>
    [Theory]
    [InlineData("opencrm", "system/email-confirmation", NotificationChannel.Email, 2)]
    [InlineData("catalog", "system/email-confirmation", NotificationChannel.Email, 1)]
    [InlineData("opencrm", "system/email-confirmation", NotificationChannel.Sms, 1)]
    [InlineData("opencrm", "system/../../_Layout", NotificationChannel.Email, 1)]
    public async Task UnknownVersionApplicationChannelOrPathIsRejected(
        string applicationId, string key, NotificationChannel channel, int version)
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            applicationId, key, channel, version, "{}", CancellationToken.None));
    }

    /// <summary> Проверяет, что дата подписки должна соответствовать типизированной модели. </summary>
    [Theory]
    [InlineData("not-a-date")]
    [InlineData("16 сентября 2026")]
    [InlineData("0001-01-01")]
    public async Task SubscriptionDateMustMatchTypedModel(string date)
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            name = "Иван",
            subscriptionName = "Тестовая",
            endDate = date,
            renewalUrl = "https://example.com/renew"
        });

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            "opencrm", "system/subscription-expiring", NotificationChannel.Email, 1, json, CancellationToken.None));
    }

    /// <summary> Проверяет, что любое настроенное приложение может использовать общий шаблон со своим брендом. </summary>
    [Fact]
    public async Task AnyConfiguredApplicationCanUseSharedTemplateAndItsOwnBrand()
    {
        using var provider = CreateProvider("fleet", "<Fleet & Company>");
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        var content = await renderer.RenderAsync(
            "fleet", "system/email-confirmation", NotificationChannel.Email, 1,
            """{"name":"Иван","confirmationUrl":"https://example.com/confirm"}""",
            CancellationToken.None);

        Assert.Equal("Подтвердите электронную почту — <Fleet & Company>", content.Subject);
        Assert.Contains("&lt;Fleet &amp; Company&gt;", content.Body);
        Assert.DoesNotContain("opencrm.example.invalid", content.Body);
    }

    /// <summary> Проверяет запрет использования шаблона вне списка разрешённого приложения. </summary>
    [Fact]
    public async Task ConfiguredApplicationCannotUseTemplateOutsideItsAllowlist()
    {
        using var provider = CreateProvider("fleet", "Fleet", ["system/password-reset"]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            "fleet", "system/email-confirmation", NotificationChannel.Email, 1,
            """{"name":"Иван","confirmationUrl":"https://example.com/confirm"}""",
            CancellationToken.None));
    }

    /// <summary> Проверяет отклонение недопустимой конфигурации бренда. </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Fleet\r\nBcc: contact@example.invalid")]
    public async Task InvalidBrandConfigurationIsRejected(string brand)
    {
        using var provider = CreateProvider("fleet", brand);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<OptionsValidationException>(() => renderer.RenderAsync(
            "fleet", "system/email-confirmation", NotificationChannel.Email, 1, "{}", CancellationToken.None));
    }

    /// <summary> Проверяет отклонение неподдерживаемых настроенных шаблонов. </summary>
    [Theory]
    [InlineData("system/unknown")]
    [InlineData("../EmailConfirmation")]
    public async Task UnsupportedConfiguredTemplateIsRejected(string template)
    {
        using var provider = CreateProvider("fleet", "Fleet", [template]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<OptionsValidationException>(() => renderer.RenderAsync(
            "fleet", template, NotificationChannel.Email, 1, "{}", CancellationToken.None));
    }

    /// <summary> Проверяет, что отсутствие профиля не даёт неявный доступ к шаблонам. </summary>
    [Fact]
    public async Task MissingProfilesDoNotGrantImplicitAccess()
    {
        var services = new ServiceCollection();
        services.AddOptions<SystemTemplateOptions>();
        services.AddSingleton<ISystemTemplateRenderer, RazorSystemTemplateRenderer>();
        services.AddRazorTemplating();
        using var provider = services.BuildServiceProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            "opencrm", "system/email-confirmation", NotificationChannel.Email, 1, "{}", CancellationToken.None));
    }

    /// <summary> Проверяет сохранение сценариев в дополнительных шаблонах OpenCRM. </summary>
    [Theory]
    [InlineData("system/login-code", """{"code":"01234567"}""", "01234567")]
    [InlineData("system/password-reset-mobile", """{"userId":"user","code":"a&b","resetUrl":"https://example.com/reset"}""", "opencrm://reset-password?userId=user&code=a%26b")]
    [InlineData("system/support-message", """{"messageId":"42","typeName":"Вопрос","topic":"Тема","text":"<script>текст</script>"}""", "Новое обращение")]
    [InlineData("system/subscription-notice", """{"kind":"upcoming","planName":"Про","endDate":"2026-09-10","daysRemaining":1,"actionUrl":"https://example.com"}""", "Остался 1 день.")]
    [InlineData("system/subscription-notice", """{"kind":"paid-expired","organizationName":"Сервис","planName":"Про","endDate":"2026-09-10","actionUrl":"https://example.com"}""", "данные сохранены")]
    [InlineData("system/subscription-notice", """{"kind":"demo-expired","organizationName":"Сервис","planName":"Демо","endDate":"2026-09-10","actionUrl":"https://example.com"}""", "Выбрать тариф")]
    [InlineData("system/subscription-notice", """{"kind":"demo-available","organizationName":"Сервис","planName":"Демо","durationDays":14,"actionUrl":"https://example.com"}""", "на 14 дней")]
    public async Task AdditionalOpenCRMTemplatesPreserveWorkflow(string key, string json, string expected)
    {
        using var provider = CreateProvider(templates: [key]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var content = await renderer.RenderAsync("opencrm", key, NotificationChannel.Email, 1, json, CancellationToken.None);
        Assert.Contains(expected, WebUtility.HtmlDecode(content.Body));
        Assert.DoesNotContain("<script>", content.Body);
        Assert.DoesNotContain("@Model", content.Body);
        Assert.False(string.IsNullOrWhiteSpace(content.Subject));
    }

    /// <summary> Проверяет настройку схемы ссылки мобильного приложения и экранирование параметров. </summary>
    [Fact]
    public async Task MobilePasswordResetUsesConfiguredApplicationScheme()
    {
        using var provider = CreateProvider(templates: ["system/password-reset-mobile"], mobileDeepLinkScheme: "sample-crm");
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var content = await renderer.RenderAsync(
            "opencrm",
            "system/password-reset-mobile",
            NotificationChannel.Email,
            1,
            """{"userId":"user id","code":"a&b","resetUrl":"https://example.com/reset"}""",
            CancellationToken.None);
        var decodedBody = WebUtility.HtmlDecode(content.Body);

        Assert.Contains("sample-crm://reset-password?userId=user%20id&code=a%26b", decodedBody);
        Assert.DoesNotContain("opencrm://reset-password", decodedBody);
    }

    /// <summary> Проверяет письма рабочего бесплатного пространства после окончания тарифа. </summary>
    [Theory]
    [InlineData("upcoming", "После окончания PRO бесплатный тариф Free останется доступен без ограничения срока")]
    [InlineData("paid-expired", "Бесплатный тариф Free доступен без ограничения срока")]
    [InlineData("demo-expired", "Бесплатный тариф Free доступен без ограничения срока")]
    public async Task SubscriptionNoticeRendersFreeWorkspaceState(string kind, string expected)
    {
        using var provider = CreateProvider(templates: ["system/subscription-notice"]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            kind,
            organizationName = "Студия",
            planName = "PRO",
            endDate = "2026-09-10",
            daysRemaining = 1,
            actionUrl = "https://example.com",
            hasFreeWorkspace = true
        });

        var content = await renderer.RenderAsync(
            "opencrm", "system/subscription-notice", NotificationChannel.Email, 1, json, CancellationToken.None);
        var decodedBody = WebUtility.HtmlDecode(content.Body);

        Assert.Contains(expected, decodedBody);
        Assert.Equal(
            kind == "upcoming"
                ? "Срок PRO скоро закончится — Free останется доступен"
                : "PRO завершён — продолжайте работу бесплатно",
            content.Subject);
        Assert.Contains("Открыть тарифы", decodedBody);
        Assert.DoesNotContain("только для чтения", decodedBody);
        Assert.DoesNotContain("Оплаченный период тарифа", decodedBody);

        if (kind == "upcoming")
        {
            Assert.Contains("До указанной даты текущие возможности сохраняются", decodedBody);
        }
        else
        {
            Assert.Contains("Данные сохранены", decodedBody);
            Assert.Contains("включая склад", decodedBody);
            Assert.Contains("управление командой", decodedBody);
            Assert.Contains("ИИ-помощник", decodedBody);
            Assert.Contains("Telegram и MAX", decodedBody);
        }
    }

    /// <summary> Проверяет отклонение некорректных данных в дополнительных шаблонах. </summary>
    [Theory]
    [InlineData("system/login-code", """{"code":"123456"}""")]
    [InlineData("system/login-code", """{"code":"<script>"}""")]
    [InlineData("system/password-reset-mobile", """{"userId":"user","code":"token","resetUrl":"javascript:alert(1)"}""")]
    [InlineData("system/subscription-notice", """{"kind":"unknown","planName":"Про","actionUrl":"https://example.com"}""")]
    [InlineData("system/subscription-notice", """{"kind":"upcoming","planName":"Про","actionUrl":"https://example.com"}""")]
    public async Task AdditionalTemplatesRejectInvalidData(string key, string json)
    {
        using var provider = CreateProvider(templates: [key]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderAsync(
            "opencrm", key, NotificationChannel.Email, 1, json, CancellationToken.None));
    }

    /// <summary> Проверяет допустимость адреса почты как имени получателя. </summary>
    [Fact]
    public async Task EmailAddressCanBeUsedAsRecipientName()
    {
        using var provider = CreateProvider();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var name = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + ".example";
        var json = System.Text.Json.JsonSerializer.Serialize(new { name, confirmationUrl = "https://example.com/confirm" });
        var content = await renderer.RenderAsync(
            "opencrm", "system/email-confirmation", NotificationChannel.Email, 1, json, CancellationToken.None);
        Assert.Contains(name, WebUtility.HtmlDecode(content.Body));
    }

    /// <summary> Проверяет, что письмо администратора использует тот же брендированный макет. </summary>
    [Fact]
    public async Task AdminEmailUsesTheSameBrandedLayoutAsLoginEmail()
    {
        using var provider = CreateProvider(templates: ["system/custom-email", "system/login-code"]);
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();
        var custom = await renderer.RenderAsync(
            "opencrm", "system/custom-email", NotificationChannel.Email, 1,
            """{"subject":"Личное письмо","htmlBody":"<p>Здравствуйте!</p><img src=\"https://example.com/photo.png\" alt=\"Фото\">"}""",
            CancellationToken.None);
        var login = await renderer.RenderAsync(
            "opencrm", "system/login-code", NotificationChannel.Email, 1,
            """{"code":"01234567"}""", CancellationToken.None);

        foreach (var content in new[] { custom, login })
        {
            Assert.Contains("class=\"email-card\"", content.Body);
            Assert.Contains("bgcolor=\"#ffffff\"", content.Body);
            Assert.Contains("font-family:Arial,Helvetica,sans-serif", content.Body);
            Assert.Contains(">O</span>", content.Body);
            Assert.Contains("opencrm.example.invalid", WebUtility.HtmlDecode(content.Body));
            Assert.Contains("@media screen", content.Body);
        }

        Assert.Contains("<p>Здравствуйте!</p>", WebUtility.HtmlDecode(custom.Body));
        Assert.Contains("src=\"https://example.com/photo.png\"", custom.Body);
        Assert.Equal(1, custom.Body.Split("<html", StringSplitOptions.None).Length - 1);
    }

    /// <summary> Проверяет, что произвольное название проекта задаёт весь бренд письма. </summary>
    [Fact]
    public async Task ProjectNameSetsEveryBrandedLineOfTheEmail()
    {
        using var provider = CreateProviderFromProjectName();
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        var content = await renderer.RenderAsync(
            "opencrm", "system/login-code", NotificationChannel.Email, 1,
            """{"code":"01234567"}""", CancellationToken.None);
        var decoded = WebUtility.HtmlDecode(content.Body);

        Assert.Equal("Код входа в СТО «Пример»", content.Subject);
        // Razor кодирует кириллицу числовыми ссылками, поэтому метка проверяется в раскодированном виде.
        Assert.Contains(">С</span>", decoded);
        Assert.Contains("<p>Ваш код для входа в СТО «Пример»:</p>", decoded);
        Assert.Contains("© СТО «Пример»", decoded);
        Assert.DoesNotContain("OpenCRM", decoded);
        Assert.DoesNotContain("opencrm.example.invalid", decoded);
    }

    /// <summary> Название без букв не создаёт эмблему: пустая подпись не выводится. </summary>
    [Fact]
    public async Task ProjectNameWithoutLettersRendersNoMark()
    {
        using var provider = CreateProviderFromProjectName("123");
        var renderer = provider.GetRequiredService<ISystemTemplateRenderer>();

        var content = await renderer.RenderAsync(
            "opencrm", "system/login-code", NotificationChannel.Email, 1,
            """{"code":"01234567"}""", CancellationToken.None);
        var decoded = WebUtility.HtmlDecode(content.Body);

        Assert.Contains("123", content.Subject);
        // Признак эмблемы в общей разметке письма: без буквы в названии она не выводится.
        Assert.DoesNotContain("border-radius:10px;background:#1f6feb", decoded);
    }

    /// <summary> Создаёт ServiceProvider как Worker: из общего названия проекта, без фиксированного бренда. </summary>
    private static ServiceProvider CreateProviderFromProjectName(string projectName = "СТО «Пример»")
    {
        var settings = new Dictionary<string, string?>
        {
            ["SystemTemplates:Applications:opencrm:Templates:0"] = "system/login-code"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, projectName);

        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<SystemTemplateOptions>, SystemTemplateOptionsValidator>();
        services.AddOptions<SystemTemplateOptions>().Bind(configuration.GetSection(SystemTemplateOptions.SectionName));
        services.AddSingleton<ISystemTemplateRenderer, RazorSystemTemplateRenderer>();
        services.AddRazorTemplating();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary> Создаёт ServiceProvider с настройками приложения и бренда для тестов. </summary>
    private static ServiceProvider CreateProvider(
        string applicationId = "opencrm",
        string brandName = "opencrm.example.invalid",
        string[]? templates = null,
        string? mobileDeepLinkScheme = null)
    {
        templates ??= ["system/email-confirmation", "system/password-reset", "system/subscription-expiring"];
        var settings = new Dictionary<string, string?>
        {
            [$"SystemTemplates:Applications:{applicationId}:BrandName"] = brandName,
            [$"SystemTemplates:Applications:{applicationId}:BrandMark"] = "O"
        };
        if (mobileDeepLinkScheme is not null)
        {
            settings[$"SystemTemplates:Applications:{applicationId}:MobileDeepLinkScheme"] = mobileDeepLinkScheme;
        }
        for (var index = 0; index < templates.Length; index++)
        {
            settings[$"SystemTemplates:Applications:{applicationId}:Templates:{index}"] = templates[index];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<SystemTemplateOptions>, SystemTemplateOptionsValidator>();
        services.AddOptions<SystemTemplateOptions>().Bind(configuration.GetSection(SystemTemplateOptions.SectionName));
        services.AddSingleton<ISystemTemplateRenderer, RazorSystemTemplateRenderer>();
        services.AddRazorTemplating();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
