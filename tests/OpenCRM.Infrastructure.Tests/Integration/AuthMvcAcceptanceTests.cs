using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Auth;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Home.Dtos;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Auth;
using OpenCRM.WebApp.Extensions;
using OpenCRM.WebApp.Infrastructure.Security;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AuthMvcAcceptanceTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private const string Password = "OpenCrm#Test1";

    [Theory]
    [InlineData("", "/personal/support")]
    [InlineData("", "/personal/support/create")]
    [InlineData("", "/personal/support?id=37")]
    [InlineData("", "/personal")]
    [InlineData("", "/organizations")]
    [InlineData("", "/vehicles")]
    [InlineData("/opencrm", "/personal/support")]
    [InlineData("/opencrm", "/personal/support/create")]
    public async Task GuestHtmlNavigationRedirectsToLoginAndPreservesReturnUrl(string pathBase, string path)
    {
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, pathBase + path);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var destination = new Uri(client.BaseAddress!, response.Headers.Location!);
        Assert.Equal(pathBase + "/auth/login", destination.AbsolutePath);
        Assert.Equal(pathBase + path, QueryHelpers.ParseQuery(destination.Query)["ReturnUrl"].ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/opencrm")]
    public async Task GuestApiRequestStillReturnsUnauthorizedWhenHtmlIsAccepted(string pathBase)
    {
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, pathBase + "/api/v1/auth/me");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task GuestPageRedirectDoesNotDependOnAcceptHeader(string? accept)
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/personal");
        if (accept != null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var destination = new Uri(client.BaseAddress!, response.Headers.Location!);
        Assert.Equal("/auth/login", destination.AbsolutePath);
        Assert.Equal("/personal", QueryHelpers.ParseQuery(destination.Query)["ReturnUrl"].ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleAccountCookieIsClearedAndPageReturnsToLogin(bool deleteUser)
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "stale-cookie");
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var saved = (await manager.FindByIdAsync(user.Id))!;
            saved.IsDeleted = true;
            var result = deleteUser ? await manager.DeleteAsync(saved) : await manager.UpdateAsync(saved);
            Assert.True(result.Succeeded);
        }

        using var api = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/api/v1/auth/me", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Null(api.Headers.Location);
        using var page = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/personal", cookies);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains(page.Headers.GetValues("Set-Cookie"), value => value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        using var login = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        Assert.Equal(HttpStatusCode.OK, login.Response.StatusCode);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("InMemory")]
    public async Task BaseInstallationShowsPasswordFlowAndAvoidsUnavailableEmailActions(string transport)
    {
        await using var app = await this.CreateHostAsync(notificationsTransport: transport);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        using var login = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        var loginHtml = await login.Response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Password\"", loginHtml);
        Assert.DoesNotContain("/auth/emailcodelogin", loginHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/auth/forgotpassword", loginHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"/organizations\"", loginHtml);
        Assert.DoesNotContain("href=\"/personal\"", loginHtml);
        Assert.Contains(Helpers.ProjectBrand.GitHubUrl, loginHtml);
        using var registration = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/register", cookies);
        Assert.DoesNotContain("/auth/emailcodelogin", await registration.Response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var code = await client.GetAsync("/auth/emailcodelogin?returnUrl=%2Fpersonal");
        Assert.Equal(HttpStatusCode.Redirect, code.StatusCode);
        var destination = new Uri(client.BaseAddress!, code.Headers.Location!);
        Assert.Equal("/auth/login", destination.AbsolutePath, ignoreCase: true);
        Assert.Equal("/personal", QueryHelpers.ParseQuery(destination.Query)["returnUrl"].ToString());
        using var forgot = await client.GetAsync("/auth/forgotpassword");
        Assert.Equal(HttpStatusCode.Redirect, forgot.StatusCode);
        Assert.Equal("/auth/login", new Uri(client.BaseAddress!, forgot.Headers.Location!).AbsolutePath, ignoreCase: true);
    }

    [Fact]
    public async Task OpeningAuthenticationFormsDoesNotSpendCredentialAttemptLimit()
    {
        await using var app = await this.CreateHostAsync(authenticationPermitLimit: 20);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var forms = new[] { "/auth/login", "/auth/register", "/auth/emailcodelogin", "/auth/forgotpassword" };
        for (var index = 0; index < 28; index++)
        {
            using var form = await AuthMvcAcceptanceTests.GetPageAsync(client, forms[index % forms.Length], cookies);
            Assert.Equal(HttpStatusCode.OK, form.Response.StatusCode);
        }

        using var login = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        for (var attempt = 0; attempt < 21; attempt++)
        {
            using var response = await AuthMvcAcceptanceTests.PostFormAsync(client, "/auth/login", new Dictionary<string, string>
            {
                ["Email"] = "missing-user@example.test",
                ["Password"] = AuthMvcAcceptanceTests.Password
            }, cookies, login.AntiforgeryToken);
            Assert.Equal(attempt < 20 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationKeepsConfirmationInsidePathBase(bool register)
    {
        const string pathBase = "/opencrm";
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = register ? null : await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "mounted-login", emailConfirmed: false);
        var path = pathBase + (register ? "/auth/register" : "/auth/login");
        using var page = await AuthMvcAcceptanceTests.GetPageAsync(client, path, cookies);

        using var response = await AuthMvcAcceptanceTests.PostFormAsync(client, path, new Dictionary<string, string>
        {
            ["Email"] = user?.Email ?? $"mounted-register-{Guid.NewGuid():N}@example.test",
            ["Password"] = AuthMvcAcceptanceTests.Password,
            ["PersonalDataProcessingConsent"] = "true",
            ["ReturnUrl"] = pathBase + "/"
        }, cookies, page.AntiforgeryToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(pathBase + EmailConfirmationMiddleware.ConfirmationPath, response.Headers.Location?.OriginalString);
        using var confirmation = await AuthMvcAcceptanceTests.GetPageAsync(client, response.Headers.Location!.OriginalString, cookies);
        Assert.Equal(HttpStatusCode.OK, confirmation.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedContactsRedirectKeepsPathBase()
    {
        const string pathBase = "/opencrm";
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "mounted-contacts", emailConfirmed: true);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies, pathBase);
        using var contacts = await AuthMvcAcceptanceTests.GetPageAsync(client, pathBase + "/contacts", cookies);

        Assert.Equal(HttpStatusCode.Redirect, contacts.Response.StatusCode);
        Assert.Equal(pathBase + "/personal/support", contacts.Response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("detailing")]
    [InlineData("service-station")]
    public async Task AuthenticatedHomeRedirectKeepsPathBase(string? scenario)
    {
        const string pathBase = "/opencrm";
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "mounted-home", emailConfirmed: true);
        var organization = new OrganizationDto { Id = Guid.NewGuid(), PrimaryScenarioCode = scenario ?? string.Empty };
        var overview = new TeamOverviewDto { Organizations = scenario == null ? [] : [organization] };
        Mock.Get(app.Services.GetRequiredService<IOrganizationQueryService>())
            .Setup(service => service.GetOverviewAsync(user.Id)).ReturnsAsync(overview);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies, pathBase);
        using var response = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, pathBase + "/", cookies);
        var destination = scenario switch
        {
            "detailing" => $"/detailing/{organization.Id:D}",
            "service-station" => $"/autoservice/{organization.Id:D}",
            _ => "/organizations"
        };

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(pathBase + destination, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/auth/resetpassword?code=synthetic-code&userId=synthetic-user")]
    [InlineData("/auth/login?returnUrl=%2Forganizations%2Fjoin%3Ftoken%3Dsynthetic-token")]
    [InlineData("/auth/confirmemail?code=synthetic-code&userId=synthetic-user")]
    [InlineData("/auth/confirmemailchange?code=synthetic-code&userId=synthetic-user&email=test%40example.test")]
    [InlineData("/email/unsubscribe?token=synthetic-token&userId=synthetic-user&email=test%40example.test")]
    public async Task SensitivePagesDoNotLoadExternalAnalyticsOrExposeReferrer(string path)
    {
        await using var app = await this.CreateHostAsync(pathBase: "/opencrm");
        using var client = app.GetTestClient();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "sensitive-page", emailConfirmed: false);
        path = path.Replace("synthetic-user", user.Id);
        using var response = await client.GetAsync("/opencrm" + path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("project-analytics", html);
        Assert.DoesNotContain("/js/analytics.js", html);
        Assert.DoesNotContain("googletagmanager", html.ToLowerInvariant());
        Assert.DoesNotContain("google-analytics", html.ToLowerInvariant());
        Assert.Contains("noindex,nofollow", html);
        Assert.Contains("<meta name=\"referrer\" content=\"no-referrer\">", html);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task OrdinaryRegistrationDoesNotLoadExternalAnalytics()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var html = await client.GetStringAsync("/auth/register");

        Assert.DoesNotContain("project-analytics", html);
        Assert.DoesNotContain("/js/analytics.js", html);
        Assert.DoesNotContain("googletagmanager", html.ToLowerInvariant());
        Assert.DoesNotContain("google-analytics", html.ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedEmailContinueLinkKeepsPathBase(bool authenticated)
    {
        const string pathBase = "/opencrm";
        await using var app = await this.CreateHostAsync(pathBase: pathBase);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "mounted-confirmation", emailConfirmed: false);
        if (authenticated)
        {
            await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies, pathBase);
        }

        var token = await AuthMvcAcceptanceTests.GenerateTokenAsync(app.Services, manager => manager.GenerateEmailConfirmationTokenAsync(user));
        var code = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
        using var response = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Get, $"{pathBase}/auth/confirmemail?userId={user.Id}&code={Uri.EscapeDataString(code)}", cookies);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var destination = authenticated ? pathBase + "/" : pathBase + "/auth/login";

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"{destination}\" class=\"btn btn-dark auth-btn\">Продолжить работу", html);
    }

    [Fact]
    public async Task UnconfirmedUserCanOpenSupportListAndSeparateCreationForm()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "unconfirmed-support", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);

        using var list = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support", cookies);
        var listHtml = await list.Response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.Response.StatusCode);
        Assert.Contains("/personal/support/create", listHtml);
        Assert.DoesNotContain("action=\"/personal/support/create\"", listHtml);

        using var create = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);
        var createHtml = await create.Response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, create.Response.StatusCode);
        Assert.Contains("name=\"TypeCode\"", createHtml);
        Assert.Contains("name=\"Topic\"", createHtml);
        Assert.Contains("name=\"Text\"", createHtml);
        Assert.Contains("name=\"IsOrganizationQuestion\"", createHtml);

        using var blockedVehicles = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/vehicles", cookies);
        Assert.Equal(EmailConfirmationMiddleware.ConfirmationPath, blockedVehicles.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SupportCreationRequiresAntiforgeryAndRedirectsToCreatedConversation()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-create", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);

        using var createPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);
        var values = new Dictionary<string, string>
        {
            ["TypeCode"] = "question",
            ["Topic"] = "Нужна помощь",
            ["Text"] = "Проверка отправки",
            ["IsOrganizationQuestion"] = "false"
        };
        using var missingAntiforgery = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Post, "/personal/support/create", cookies, new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);

        using var created = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/personal/support/create", values, cookies, createPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/personal/support?id=37", created.Headers.Location?.OriginalString);
        app.SupportMessages.Verify(service => service.CreateAsync(It.Is<SupportMessageCreateDto>(request =>
            request.UserId == user.Id && request.TypeCode == "question" &&
            request.Topic == "Нужна помощь" && request.Text == "Проверка отправки")), Times.Once);
    }

    [Fact]
    public async Task InvalidSupportCreationKeepsEnteredValuesAndDoesNotCallService()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "invalid-support", emailConfirmed: false);
        var organizationId = Guid.NewGuid();
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        using var createPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);
        var topic = new string('t', 181);
        app.SupportMessages
            .Setup(service => service.GetInboxAsync(user.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportInboxDto
            {
                Organizations = [new SupportOrganizationOptionDto { Id = organizationId, Name = "Моя организация" }]
            });

        using var response = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/personal/support/create",
            new Dictionary<string, string>
            {
                ["TypeCode"] = "question",
                ["Topic"] = topic,
                ["Text"] = "Текст должен сохраниться",
                ["IsOrganizationQuestion"] = "true",
                ["OrganizationId"] = organizationId.ToString()
            },
            cookies,
            createPage.AntiforgeryToken);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value=\"question\"", html);
        Assert.Contains(topic, html);
        Assert.Contains("Текст должен сохраниться", html);
        Assert.Contains("id=\"support-organization-select\"", html);
        Assert.Contains(organizationId.ToString(), html);
        Assert.Contains("Укажите тему и сообщение", html);
        app.SupportMessages.Verify(service => service.CreateAsync(It.IsAny<SupportMessageCreateDto>()), Times.Never);
    }

    [Fact]
    public async Task OrganizationSupportCreationRequiresOrganizationAccess()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "organization-support", emailConfirmed: false);
        var organizationId = Guid.NewGuid();
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        app.SupportMessages
            .Setup(service => service.GetInboxAsync(user.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportInboxDto
            {
                Organizations = [new SupportOrganizationOptionDto { Id = organizationId, Name = "Организация пользователя" }]
            });
        using var createPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);

        using var response = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/personal/support/create",
            new Dictionary<string, string>
            {
                ["TypeCode"] = "question",
                ["Topic"] = "Вопрос по организации",
                ["Text"] = "Не должен уйти без доступа",
                ["IsOrganizationQuestion"] = "true",
                ["OrganizationId"] = organizationId.ToString()
            },
            cookies,
            createPage.AntiforgeryToken);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(organizationId.ToString(), html);
        Assert.Contains("Выбранная организация недоступна", html);
        app.SupportMessages.Verify(service => service.HasOrganizationAccessAsync(user.Id, organizationId, It.IsAny<CancellationToken>()), Times.Once);
        app.SupportMessages.Verify(service => service.CreateAsync(It.IsAny<SupportMessageCreateDto>()), Times.Never);
    }

    [Fact]
    public async Task UnconfirmedUserCanReplyToOwnSupportAndCannotReplyToUnknownConversation()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-reply", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        using var supportPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support", cookies);
        app.SupportMessages
            .Setup(service => service.ReplyAsync(user.Id, 37, "Ответ клиента", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SupportMessageReplyResult.Succeeded);
        app.SupportMessages
            .Setup(service => service.ReplyAsync(user.Id, 999, "Чужое обращение", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SupportMessageReplyResult.NotFound);

        using var reply = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/personal/support/reply",
            new Dictionary<string, string> { ["Id"] = "37", ["Text"] = "Ответ клиента" },
            cookies,
            supportPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.Equal("/personal/support?id=37", reply.Headers.Location?.OriginalString);
        app.SupportMessages.Verify(service => service.ReplyAsync(user.Id, 37, "Ответ клиента", It.IsAny<CancellationToken>()), Times.Once);

        using var unknownReply = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/personal/support/reply",
            new Dictionary<string, string> { ["Id"] = "999", ["Text"] = "Чужое обращение" },
            cookies,
            supportPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownReply.StatusCode);
    }

    [Fact]
    public async Task FailedSupportServiceKeepsFormValuesAndShowsServiceError()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-service-error", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        using var createPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);
        app.SupportMessages
            .Setup(service => service.CreateAsync(It.IsAny<SupportMessageCreateDto>()))
            .ReturnsAsync(new SupportMessageCreateResultDto
            {
                Succeeded = false,
                ErrorMessage = "Не удалось сохранить обращение."
            });

        using var response = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/personal/support/create",
            new Dictionary<string, string>
            {
                ["TypeCode"] = "question",
                ["Topic"] = "Тема при ошибке сервиса",
                ["Text"] = "Сообщение при ошибке сервиса",
                ["IsOrganizationQuestion"] = "false"
            },
            cookies,
            createPage.AntiforgeryToken);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Не удалось сохранить обращение.", html);
        Assert.Contains("Тема при ошибке сервиса", html);
        Assert.Contains("Сообщение при ошибке сервиса", html);
    }

    [Fact]
    public async Task AnonymousUserMustSignInToOpenSupportCreation()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();

        using var response = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/personal/support/create", cookies);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login", new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
    }

    [Fact]
    public async Task ApiRegistrationSendsConfirmationAndRestrictsIssuedTokenUntilEmailIsConfirmed()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        using var registration = await client.PostAsync("/api/v1/auth/register", new StringContent(
            JsonSerializer.Serialize(new
            {
                email = $"api-email-gate-{Guid.NewGuid():N}@example.test",
                password = Password,
                personalDataProcessingConsent = true
            }), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        using var body = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var blocked = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var email = Assert.Single(app.EmailMessages.Messages);
        Assert.Equal("system/email-confirmation", email.TemplateKey);
        var confirmationUri = new Uri(email.Data.GetProperty("confirmationUrl").GetString()!);
        using var confirmation = await client.GetAsync(confirmationUri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        using var allowed = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task OptionalEmailConfirmationAllowsMvcRegistrationAndLoginWithoutSendingEmail()
    {
        const string pathBase = "/opencrm";
        await using var app = await this.CreateHostAsync(pathBase: pathBase, requireEmailConfirmation: false);
        using var client = app.GetTestClient();
        var registrationCookies = new Dictionary<string, string>();
        var registeredEmail = $"optional-register-{Guid.NewGuid():N}@example.test";
        using var registerPage = await AuthMvcAcceptanceTests.GetPageAsync(
            client,
            pathBase + "/auth/register",
            registrationCookies);
        using var registration = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            pathBase + "/auth/register?returnUrl=%2Fopencrm%2Forganizations%2Fcreate",
            new Dictionary<string, string>
            {
                ["Email"] = registeredEmail,
                ["Password"] = Password,
                ["PersonalDataProcessingConsent"] = "true"
            },
            registrationCookies,
            registerPage.AntiforgeryToken);

        Assert.Equal(HttpStatusCode.Redirect, registration.StatusCode);
        Assert.Equal(pathBase + "/organizations/create", registration.Headers.Location?.OriginalString);
        Assert.Empty(app.EmailMessages.Messages);
        using var registeredSession = await AuthMvcAcceptanceTests.SendAsync(
            client,
            HttpMethod.Get,
            pathBase + "/test/current-user",
            registrationCookies);
        var registeredUserId = await registeredSession.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, registeredSession.StatusCode);
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(
            app.Services,
            registeredUserId,
            user => !user.EmailConfirmed));

        var loginUser = await AuthMvcAcceptanceTests.CreateUserAsync(
            app.Services,
            "optional-login",
            emailConfirmed: false);
        var loginCookies = new Dictionary<string, string>();
        using var loginPage = await AuthMvcAcceptanceTests.GetPageAsync(
            client,
            pathBase + "/auth/login",
            loginCookies);
        using var login = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            pathBase + "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = loginUser.Email!,
                ["Password"] = Password,
                ["ReturnUrl"] = pathBase + "/organizations"
            },
            loginCookies,
            loginPage.AntiforgeryToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(pathBase + "/organizations", login.Headers.Location?.OriginalString);
        using var loggedInSession = await AuthMvcAcceptanceTests.SendAsync(
            client,
            HttpMethod.Get,
            pathBase + "/test/current-user",
            loginCookies);
        Assert.Equal(HttpStatusCode.OK, loggedInSession.StatusCode);
        Assert.Equal(loginUser.Id, await loggedInSession.Content.ReadAsStringAsync());
        Assert.Empty(app.EmailMessages.Messages);
    }

    [Fact]
    public async Task OptionalEmailConfirmationAllowsApiRegistrationAndLoginWithoutSendingEmail()
    {
        await using var app = await this.CreateHostAsync(requireEmailConfirmation: false);
        using var client = app.GetTestClient();
        var email = $"optional-api-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsync("/api/v1/auth/register", new StringContent(
            JsonSerializer.Serialize(new
            {
                email,
                password = Password,
                personalDataProcessingConsent = true
            }), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        Assert.Empty(app.EmailMessages.Messages);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var registrationToken = registrationBody.RootElement.GetProperty("accessToken").GetString();
        var userId = registrationBody.RootElement.GetProperty("user").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(registrationToken));
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(app.Services, userId!, user => !user.EmailConfirmed));

        using var loginClient = app.GetTestClient();
        using var login = await loginClient.PostAsync("/api/v1/auth/login", new StringContent(
            JsonSerializer.Serialize(new { email, password = Password }),
            System.Text.Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        loginClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            loginBody.RootElement.GetProperty("accessToken").GetString());
        using var currentUser = await loginClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);
        Assert.Empty(app.EmailMessages.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationRequiresEmailConfirmationEvenWhenDeliveryFails(bool deliveryFails)
    {
        await using var app = await this.CreateHostAsync();
        app.EmailMessages.ThrowOnSend = deliveryFails;
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var email = $"new-email-gate-{Guid.NewGuid():N}@example.test";
        using var registerPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/register", cookies);
        using var registration = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/register?returnUrl=%2Forganizations%2Fcreate",
            new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = Password,
                ["PersonalDataProcessingConsent"] = "true"
            },
            cookies,
            registerPage.AntiforgeryToken);

        Assert.Equal(HttpStatusCode.Redirect, registration.StatusCode);
        Assert.Equal(EmailConfirmationMiddleware.ConfirmationPath, registration.Headers.Location?.OriginalString);
        using var confirmationPage = await AuthMvcAcceptanceTests.GetPageAsync(client, EmailConfirmationMiddleware.ConfirmationPath, cookies);
        Assert.Equal(HttpStatusCode.OK, confirmationPage.Response.StatusCode);
        var html = WebUtility.HtmlDecode(await confirmationPage.Response.Content.ReadAsStringAsync());
        Assert.Contains(email, html);
        Assert.Contains("Подтвердите email, чтобы продолжить", html);
        Assert.Contains("/css/auth", html);
        Assert.Contains("data-retry-after=\"", html);
        Assert.Equal(deliveryFails ? 0 : 1, app.EmailMessages.Messages.Count);
        using var resendAfterRegistration = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/auth/verify-email/resend", new Dictionary<string, string>(),
            cookies, confirmationPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, resendAfterRegistration.StatusCode);
        Assert.Equal(deliveryFails ? 0 : 1, app.EmailMessages.Messages.Count);
        Assert.Contains(deliveryFails ? "письмо подтверждения не отправлено" : "Письмо для подтверждения email отправлено", html);
        if (deliveryFails)
        {
            app.EmailMessages.ThrowOnSend = false;
            using var retryAfterFailure = await AuthMvcAcceptanceTests.PostFormAsync(
                client, "/auth/verify-email/resend", new Dictionary<string, string>(),
                cookies, confirmationPage.AntiforgeryToken);
            Assert.Equal(HttpStatusCode.Redirect, retryAfterFailure.StatusCode);
            Assert.Single(app.EmailMessages.Messages);
        }

        using var businessPage = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/organizations/create", cookies);
        Assert.Equal(EmailConfirmationMiddleware.ConfirmationPath, businessPage.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ExistingAccountRegainsAccessInSameSessionOnlyAfterConfirmingEmail()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "existing-unconfirmed", emailConfirmed: false);
        using var loginPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        using var login = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/auth/login", new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["Password"] = Password,
                ["ReturnUrl"] = "/vehicles"
            }, cookies, loginPage.AntiforgeryToken);

        Assert.Equal(EmailConfirmationMiddleware.ConfirmationPath, login.Headers.Location?.OriginalString);
        using var blockedApi = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/api/v1/vehicles", cookies);
        Assert.Equal(HttpStatusCode.Forbidden, blockedApi.StatusCode);
        using var verificationPage = await AuthMvcAcceptanceTests.GetPageAsync(client, EmailConfirmationMiddleware.ConfirmationPath, cookies);
        using var noAntiforgery = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Post, "/auth/verify-email/resend", cookies, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode);
        Assert.Empty(app.EmailMessages.Messages);

        using var resend = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/auth/verify-email/resend",
            new Dictionary<string, string> { ["email"] = "contact@example.invalid" },
            cookies, verificationPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, resend.StatusCode);
        var message = Assert.Single(app.EmailMessages.Messages);
        Assert.Equal(user.Email, message.Email);
        var confirmationUri = new Uri(message.Data.GetProperty("confirmationUrl").GetString()!);

        using var repeatedSend = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/auth/verify-email/resend", new Dictionary<string, string>(),
            cookies, verificationPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, repeatedSend.StatusCode);
        Assert.Single(app.EmailMessages.Messages);
        using var cooldownPage = await AuthMvcAcceptanceTests.GetPageAsync(client, EmailConfirmationMiddleware.ConfirmationPath, cookies);
        var cooldownHtml = await cooldownPage.Response.Content.ReadAsStringAsync();
        Assert.Contains("disabled=\"disabled\"", cooldownHtml);

        using var legacyResend = await AuthMvcAcceptanceTests.PostFormAsync(
            client, "/auth/resendemailconfirmation", new Dictionary<string, string> { ["email"] = user.Email! },
            cookies, verificationPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, legacyResend.StatusCode);
        Assert.Single(app.EmailMessages.Messages);

        using var invalidLink = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Get, $"/auth/confirmemail?userId={user.Id}&code=broken-token", cookies);
        Assert.Equal(HttpStatusCode.OK, invalidLink.StatusCode);
        using var stillBlocked = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(EmailConfirmationMiddleware.ConfirmationPath, stillBlocked.Headers.Location?.OriginalString);

        using var confirmed = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, confirmationUri.PathAndQuery, cookies);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var unlocked = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(user.Id, await unlocked.Content.ReadAsStringAsync());
        using var continueResponse = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, EmailConfirmationMiddleware.ConfirmationPath, cookies);
        Assert.Equal(HttpStatusCode.Redirect, continueResponse.StatusCode);
        Assert.Equal("/", continueResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task RegistrationPageDoesNotOfferAccountPromoCodes()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        using var page = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/register", cookies);

        Assert.Equal(HttpStatusCode.OK, page.Response.StatusCode);
        var html = await page.Response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"PromoCode\"", html);
        Assert.DoesNotContain("У меня есть промокод", html);
        Assert.Contains("name=\"Email\"", html);
        Assert.NotEmpty(page.AntiforgeryToken);
    }

    [Fact]
    public async Task LoginMvcPreservesOnlyLocalReturnUrlsAndRequiresAntiforgery()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        using var page = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login?returnUrl=%2Fworkspace%2Ftoday", cookies);

        Assert.Equal(HttpStatusCode.OK, page.Response.StatusCode);
        var html = await page.Response.Content.ReadAsStringAsync();
        Assert.Contains("Войти в аккаунт", html);
        Assert.Contains("/auth/forgotPassword", html);
        Assert.Contains("returnUrl", html);
        Assert.Contains("%2Fworkspace%2Ftoday", html, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(page.AntiforgeryToken);

        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "login");
        using var anonymousPost = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password,
                ["ReturnUrl"] = "https://example.test/steal"
            },
            cookies,
            page.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, anonymousPost.StatusCode);
        Assert.Equal("/", anonymousPost.Headers.Location?.OriginalString);

        var localUser = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "local-return");
        using var localPage = await AuthMvcAcceptanceTests.GetPageAsync(
            client,
            "/auth/login?returnUrl=%2Fworkspace%2Ftoday",
            cookies);
        using var localLogin = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = localUser.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password,
                ["ReturnUrl"] = "/workspace/today"
            },
            cookies,
            localPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, localLogin.StatusCode);
        Assert.Equal("/workspace/today", localLogin.Headers.Location?.OriginalString);

        using var missingAntiforgery = await client.PostAsync(
            "/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password
            }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
    }

    [Fact]
    public async Task PasswordRecoveryHidesUnknownAndDeletedAccountsAndUsesSingleUseIdentityTokens()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "reset");
        var deletedUser = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "deleted-reset");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            deletedUser = await manager.FindByIdAsync(deletedUser.Id) ?? throw new InvalidOperationException("Пользователь не найден.");
            deletedUser.IsDeleted = true;
            Assert.True((await manager.UpdateAsync(deletedUser)).Succeeded);
        }

        using var forgotPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/forgotpassword", cookies);
        Assert.Equal(HttpStatusCode.OK, forgotPage.Response.StatusCode);
        Assert.Contains("/auth/login", await forgotPage.Response.Content.ReadAsStringAsync());
        using var unknownResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/forgotpassword",
            new Dictionary<string, string> { ["Email"] = $"missing-{Guid.NewGuid():N}@example.test" },
            cookies,
            forgotPage.AntiforgeryToken);
        using var deletedResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/forgotpassword",
            new Dictionary<string, string> { ["Email"] = deletedUser.Email! },
            cookies,
            forgotPage.AntiforgeryToken);
        using var recoveryResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/forgotpassword",
            new Dictionary<string, string> { ["Email"] = user.Email! },
            cookies,
            forgotPage.AntiforgeryToken);

        Assert.Equal(unknownResponse.Headers.Location?.OriginalString, deletedResponse.Headers.Location?.OriginalString);
        Assert.Equal(deletedResponse.Headers.Location?.OriginalString, recoveryResponse.Headers.Location?.OriginalString);
        Assert.Equal("/auth/forgotpasswordconfirmation", recoveryResponse.Headers.Location?.OriginalString, ignoreCase: true);
        using var malformedForgotPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/forgotpassword", cookies);
        using var malformedForgotResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/forgotpassword",
            new Dictionary<string, string> { ["Email"] = "not-an-email" },
            cookies,
            malformedForgotPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, malformedForgotResponse.StatusCode);
        Assert.Single(app.EmailMessages.Messages);
        var resetMessage = Assert.Single(app.EmailMessages.Messages);
        Assert.Equal(user.Email, resetMessage.Email);
        Assert.Equal("system/password-reset", resetMessage.TemplateKey);
        var resetUrl = resetMessage.Data.GetProperty("resetUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resetUrl));
        var resetUri = new Uri(resetUrl!, UriKind.Absolute);
        var resetQuery = QueryHelpers.ParseQuery(resetUri.Query);
        var encodedCode = resetQuery["code"].ToString();
        var resetPath = $"/auth/resetpassword?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedCode)}";
        using var resetPage = await AuthMvcAcceptanceTests.GetPageAsync(client, resetPath, cookies);
        Assert.Equal(HttpStatusCode.OK, resetPage.Response.StatusCode);
        var resetHtml = await resetPage.Response.Content.ReadAsStringAsync();
        Assert.Contains(user.Email!, resetHtml);
        Assert.Contains("name=\"Code\"", resetHtml);

        using var resetResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/resetpassword",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["UserId"] = user.Id,
                ["Code"] = encodedCode,
                ["Password"] = "New-OpenCRM#2026",
                ["ConfirmPassword"] = "New-OpenCRM#2026"
            },
            cookies,
            resetPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, resetResponse.StatusCode);
        Assert.Equal("/auth/resetpasswordconfirmation", resetResponse.Headers.Location?.OriginalString, ignoreCase: true);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var savedUser = await manager.FindByIdAsync(user.Id);
            Assert.NotNull(savedUser);
            Assert.True(await manager.CheckPasswordAsync(savedUser, "New-OpenCRM#2026"));
        }

        using var repeatedPage = await AuthMvcAcceptanceTests.GetPageAsync(client, resetPath, cookies);
        using var repeatedResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/resetpassword",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["UserId"] = user.Id,
                ["Code"] = encodedCode,
                ["Password"] = "Another-OpenCRM#2026",
                ["ConfirmPassword"] = "Another-OpenCRM#2026"
            },
            cookies,
            repeatedPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.Contains("name=\"Password\"", await repeatedResponse.Content.ReadAsStringAsync());

        using var invalidTokenPage = await AuthMvcAcceptanceTests.GetPageAsync(
            client,
            $"/auth/resetpassword?userId={Uri.EscapeDataString(user.Id)}&code=not-a-token",
            cookies);
        using var invalidTokenResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/resetpassword",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["UserId"] = user.Id,
                ["Code"] = "not-a-token",
                ["Password"] = "Another-OpenCRM#2026",
                ["ConfirmPassword"] = "Another-OpenCRM#2026"
            },
            cookies,
            invalidTokenPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, invalidTokenResponse.StatusCode);
        Assert.Contains("name=\"Password\"", await invalidTokenResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpiredIdentityPasswordResetTokenIsRejectedWithoutChangingPassword()
    {
        await using var app = await this.CreateHostAsync(TimeSpan.Zero);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "expired-reset");
        using var forgotPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/forgotpassword", cookies);
        using var forgotResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/forgotpassword",
            new Dictionary<string, string> { ["Email"] = user.Email! },
            cookies,
            forgotPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, forgotResponse.StatusCode);
        var message = Assert.Single(app.EmailMessages.Messages);
        var resetUri = new Uri(message.Data.GetProperty("resetUrl").GetString()!, UriKind.Absolute);
        var resetQuery = QueryHelpers.ParseQuery(resetUri.Query);
        var encodedCode = resetQuery["code"].ToString();
        var resetPath = $"/auth/resetpassword?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedCode)}";

        using var resetPage = await AuthMvcAcceptanceTests.GetPageAsync(client, resetPath, cookies);
        using var expiredResponse = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/resetpassword",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["UserId"] = user.Id,
                ["Code"] = encodedCode,
                ["Password"] = "New-OpenCRM#2026",
                ["ConfirmPassword"] = "New-OpenCRM#2026"
            },
            cookies,
            resetPage.AntiforgeryToken);

        Assert.Equal(HttpStatusCode.OK, expiredResponse.StatusCode);
        Assert.Contains("name=\"Password\"", await expiredResponse.Content.ReadAsStringAsync());
        await using var verification = app.Services.CreateAsyncScope();
        var manager = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var savedUser = await manager.FindByIdAsync(user.Id);
        Assert.NotNull(savedUser);
        Assert.True(await manager.CheckPasswordAsync(savedUser, AuthMvcAcceptanceTests.Password));
    }

    [Fact]
    public async Task EmailConfirmationAndChangePreserveRoutesAndUseSingleUseIdentityTokens()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "confirm-email", emailConfirmed: false);
        var emailToken = await AuthMvcAcceptanceTests.GenerateTokenAsync(
            app.Services,
            manager => manager.GenerateEmailConfirmationTokenAsync(user));
        var emailCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(emailToken));

        using var confirmation = await client.GetAsync(
            $"/auth/confirmemail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(emailCode)}");
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Contains(
            "Спасибо за подтверждение адреса электронной почты",
            WebUtility.HtmlDecode(await confirmation.Content.ReadAsStringAsync()));
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(app.Services, user.Id, saved => saved.EmailConfirmed));

        using var repeatedConfirmation = await client.GetAsync(
            $"/auth/confirmemail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(emailCode)}");
        Assert.Equal(HttpStatusCode.OK, repeatedConfirmation.StatusCode);
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(app.Services, user.Id, saved => saved.EmailConfirmed));
        using var malformedConfirmation = await client.GetAsync(
            $"/auth/confirmemail?userId={Uri.EscapeDataString(user.Id)}&code=broken-token");
        Assert.Equal(HttpStatusCode.OK, malformedConfirmation.StatusCode);
        Assert.Contains(
            "Ошибка подтверждения адреса электронной почты",
            WebUtility.HtmlDecode(await malformedConfirmation.Content.ReadAsStringAsync()),
            StringComparison.OrdinalIgnoreCase);

        var secondUser = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "email-change");
        using var authenticatedClient = app.GetTestClient();
        var loginCookies = new Dictionary<string, string>();
        using var loginPage = await AuthMvcAcceptanceTests.GetPageAsync(authenticatedClient, "/auth/login", loginCookies);
        using var login = await AuthMvcAcceptanceTests.PostFormAsync(
            authenticatedClient,
            "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = secondUser.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password,
                ["ReturnUrl"] = "/"
            },
            loginCookies,
            loginPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var newEmail = $"changed-{Guid.NewGuid():N}@example.test";
        var changeToken = await AuthMvcAcceptanceTests.GenerateTokenAsync(
            app.Services,
            manager => manager.GenerateChangeEmailTokenAsync(secondUser, newEmail));
        var changeCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(changeToken));
        using var changed = await AuthMvcAcceptanceTests.GetPageAsync(
            authenticatedClient,
            $"/auth/confirmemailchange?userId={Uri.EscapeDataString(secondUser.Id)}&email={Uri.EscapeDataString(newEmail)}&code={Uri.EscapeDataString(changeCode)}",
            loginCookies);
        Assert.Equal(HttpStatusCode.OK, changed.Response.StatusCode);
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(
            app.Services,
            secondUser.Id,
            saved => saved.Email == newEmail && saved.UserName == newEmail));

        using var repeatedChange = await AuthMvcAcceptanceTests.GetPageAsync(
            authenticatedClient,
            $"/auth/confirmemailchange?userId={Uri.EscapeDataString(secondUser.Id)}&email={Uri.EscapeDataString(newEmail)}&code={Uri.EscapeDataString(changeCode)}",
            loginCookies);
        Assert.Equal(HttpStatusCode.OK, repeatedChange.Response.StatusCode);
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(
            app.Services,
            secondUser.Id,
            saved => saved.Email == newEmail && saved.UserName == newEmail));

        using var malformedChange = await AuthMvcAcceptanceTests.GetPageAsync(
            authenticatedClient,
            $"/auth/confirmemailchange?userId={Uri.EscapeDataString(secondUser.Id)}&email=malformed-{Guid.NewGuid():N}%40example.test&code=broken-token",
            loginCookies);
        Assert.Equal(HttpStatusCode.OK, malformedChange.Response.StatusCode);
        Assert.Contains(
            "Ошибка при изменении адреса электронной почты",
            WebUtility.HtmlDecode(await malformedChange.Response.Content.ReadAsStringAsync()),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(
            app.Services,
            secondUser.Id,
            saved => saved.Email == newEmail && saved.UserName == newEmail));
    }

    [Fact]
    public async Task ExpiredIdentityEmailTokensDoNotConfirmOrChangeEmail()
    {
        await using var app = await this.CreateHostAsync(TimeSpan.Zero);
        using var client = app.GetTestClient();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "expired-email", emailConfirmed: false);
        var newEmail = $"expired-change-{Guid.NewGuid():N}@example.test";
        var tokens = await AuthMvcAcceptanceTests.GenerateTokenAsync(
            app.Services,
            async manager => (
                await manager.GenerateEmailConfirmationTokenAsync(user),
                await manager.GenerateChangeEmailTokenAsync(user, newEmail)));
        var confirmationCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(tokens.Item1));
        var changeCode = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(tokens.Item2));

        using var confirmation = await client.GetAsync(
            $"/auth/confirmemail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(confirmationCode)}");
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Contains("Ошибка подтверждения", WebUtility.HtmlDecode(await confirmation.Content.ReadAsStringAsync()));
        Assert.False(await AuthMvcAcceptanceTests.ReadUserAsync(app.Services, user.Id, saved => saved.EmailConfirmed));

        using var change = await client.GetAsync(
            $"/auth/confirmemailchange?userId={Uri.EscapeDataString(user.Id)}&email={Uri.EscapeDataString(newEmail)}&code={Uri.EscapeDataString(changeCode)}");
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Contains("Ошибка при изменении адреса", WebUtility.HtmlDecode(await change.Content.ReadAsStringAsync()));
        Assert.True(await AuthMvcAcceptanceTests.ReadUserAsync(
            app.Services,
            user.Id,
            saved => saved.Email != newEmail && saved.UserName != newEmail));
    }

    [Fact]
    public async Task LogoutAndImpersonationRequireAntiforgeryAndRestoreOriginalIdentity()
    {
        await using var app = await this.CreateHostAsync();
        var (administrator, target) = await AuthMvcAcceptanceTests.CreateImpersonationUsersAsync(app.Services);
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        using var loginPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        using var login = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = administrator.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password,
                ["ReturnUrl"] = "/admin"
            },
            cookies,
            loginPage.AntiforgeryToken);
        Assert.Equal("/admin", login.Headers.Location?.OriginalString);

        using var authenticatedPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        using var missingImpersonationToken = await AuthMvcAcceptanceTests.PostJsonAsync(
            client,
            "/auth/impersonateuser",
            JsonSerializer.Serialize(new { userId = target.Id }),
            cookies,
            antiforgeryToken: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingImpersonationToken.StatusCode);

        using var impersonation = await AuthMvcAcceptanceTests.PostJsonAsync(
            client,
            "/auth/impersonateuser",
            JsonSerializer.Serialize(new { userId = target.Id }),
            cookies,
            authenticatedPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, impersonation.StatusCode);
        AuthMvcAcceptanceTests.AddResponseCookies(impersonation, cookies);
        Assert.Contains("ImpersonatorId", cookies.Keys);
        using var currentTarget = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(HttpStatusCode.OK, currentTarget.StatusCode);
        Assert.Equal(target.Id, await currentTarget.Content.ReadAsStringAsync());

        using var targetPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        using var restore = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/stopimpersonation",
            new Dictionary<string, string>(),
            cookies,
            targetPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        AuthMvcAcceptanceTests.AddResponseCookies(restore, cookies);
        Assert.DoesNotContain("ImpersonatorId", cookies.Keys);
        using var currentAdministrator = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(administrator.Id, await currentAdministrator.Content.ReadAsStringAsync());

        using var logoutPage = await AuthMvcAcceptanceTests.GetPageAsync(client, "/auth/login", cookies);
        using var missingLogoutToken = await client.PostAsync(
            "/auth/logout?returnUrl=%2Fgoodbye",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, missingLogoutToken.StatusCode);
        using var stillAuthenticated = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(administrator.Id, await stillAuthenticated.Content.ReadAsStringAsync());

        using var logout = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            "/auth/logout?returnUrl=%2Fgoodbye",
            new Dictionary<string, string>(),
            cookies,
            logoutPage.AntiforgeryToken);
        Assert.Equal("/goodbye", logout.Headers.Location?.OriginalString);
        using var anonymous = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, "/test/current-user", cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupportMultipartCreationChecksAttachmentsBeforeCallingService(bool isValid)
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-files", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        using var page = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support/create", cookies);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(page.AntiforgeryToken), "__RequestVerificationToken" },
            { new StringContent("bug"), "TypeCode" },
            { new StringContent("Ошибка на странице"), "Topic" },
            { new StringContent("Прикладываю скриншот и описание ошибки"), "Text" },
            { new ByteArrayContent(isValid ? [137, 80, 78, 71, 13, 10, 26, 10] : [1, 2, 3]), "Attachments", "screen.png" },
            { new ByteArrayContent("Описание ошибки"u8.ToArray()), "Attachments", "details.txt" }
        };

        using var response = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Post, "/personal/support/create", cookies, content);

        Assert.Equal(isValid ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        if (isValid)
        {
            app.SupportMessages.Verify(service => service.CreateAsync(It.Is<SupportMessageCreateDto>(dto =>
                dto.UserId == user.Id && dto.Attachments.Count == 2 && dto.Attachments[0].FileName == "screen.png"
                && dto.Attachments[1].FileName == "details.txt")), Times.Once);

            return;
        }

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Содержимое должно соответствовать формату", html);
        Assert.Contains("Ошибка на странице", html);
        app.SupportMessages.Verify(service => service.CreateAsync(It.IsAny<SupportMessageCreateDto>()), Times.Never);
    }

    [Fact]
    public async Task UnconfirmedUserCanReplyWithSupportAttachments()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-file-reply", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        using var page = await AuthMvcAcceptanceTests.GetPageAsync(client, "/personal/support", cookies);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(page.AntiforgeryToken), "__RequestVerificationToken" },
            { new StringContent("37"), "Id" },
            { new StringContent("Дополняю описание"), "Text" },
            { new ByteArrayContent("Новые подробности"u8.ToArray()), "Attachments", "details.log" }
        };

        using var response = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Post, "/personal/support/reply", cookies, content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        app.SupportMessages.Verify(service => service.ReplyAsync(user.Id, 37, "Дополняю описание",
            It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<SupportAttachmentUploadDto>>(files =>
                files.Count == 1 && files[0].FileName == "details.log")), Times.Once);
    }

    [Fact]
    public async Task SupportDownloadRequiresSignInAndUsesPrivateDownloadHeaders()
    {
        await using var app = await this.CreateHostAsync();
        using var client = app.GetTestClient();
        var cookies = new Dictionary<string, string>();
        var id = Guid.NewGuid();
        using var anonymous = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Get, $"/personal/support/attachments/{id}", cookies);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        var signIn = new Uri(client.BaseAddress!, anonymous.Headers.Location!);
        Assert.Equal("/auth/login", signIn.AbsolutePath);
        Assert.Equal($"/personal/support/attachments/{id}", QueryHelpers.ParseQuery(signIn.Query)["ReturnUrl"].ToString());

        var user = await AuthMvcAcceptanceTests.CreateUserAsync(app.Services, "support-download", emailConfirmed: false);
        await AuthMvcAcceptanceTests.SignInAsync(client, user, cookies);
        var attachments = Mock.Get(app.Services.GetRequiredService<ISupportAttachmentService>());
        attachments.Setup(service => service.GetForUserAsync(user.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportAttachmentContentDto { FileName = "details.txt", Content = "Подробности"u8.ToArray() });

        using var response = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Get, $"/personal/support/attachments/{id}", cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("Подробности", await response.Content.ReadAsStringAsync());

        using var missing = await AuthMvcAcceptanceTests.SendAsync(
            client, HttpMethod.Get, $"/personal/support/attachments/{Guid.NewGuid()}", cookies);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<TestApp> CreateHostAsync(
        TimeSpan? tokenLifespan = null,
        string pathBase = "",
        bool requireEmailConfirmation = true,
        int? authenticationPermitLimit = null,
        string? notificationsTransport = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentitySettings:SignIn:RequireConfirmedAccount"] = "false",
            ["IdentitySettings:SignIn:RequireConfirmedEmail"] = "false",
            [EmailConfirmationPolicy.ConfigurationKey] = requireEmailConfirmation.ToString(),
            ["Notifications:Transport"] = notificationsTransport
        });
        var tokenLifespanState = new TokenLifespanState(tokenLifespan ?? TimeSpan.FromHours(1));
        var emailMessages = new RecordingEmailNotificationService();
        var supportMessages = new Mock<ISupportMessageService>();
        supportMessages
            .Setup(service => service.GetInboxAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportInboxDto());
        supportMessages
            .Setup(service => service.CreateAsync(It.IsAny<SupportMessageCreateDto>()))
            .ReturnsAsync(new SupportMessageCreateResultDto { Succeeded = true, MessageId = 37 });
        supportMessages
            .Setup(service => service.HasOrganizationAccessAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddOpenCRMIdentity(builder.Configuration);
        builder.Services.AddOpenCRMAuthInfrastructure(builder.Configuration);
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = tokenLifespanState.Value);
        builder.Services.AddDataProtection().SetApplicationName($"AuthMvcAcceptance-{Guid.NewGuid():N}");
        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
        });
        builder.Services.AddOpenCRMAdminModule();
        // Представления получают бренд через @inject; в приложении его регистрирует AddOpenCRMPresentation.
        builder.Services.AddSingleton(Helpers.ProjectBrand.Read(key => builder.Configuration[key]));
        builder.Services.AddSingleton<IEmailNotificationService>(emailMessages);
        var unsubscribe = new Mock<IEmailUnsubscribeService>();
        unsubscribe.Setup(service => service.UnsubscribeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenCRM.Services.Interfaces.Emails.Dtos.EmailUnsubscribeResult.InvalidLink);
        builder.Services.AddSingleton(unsubscribe.Object);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<EmailConfirmationDelivery>();
        builder.Services.AddScoped<IUsersService>(_ =>
        {
            var users = new Mock<IUsersService>();
            users.Setup(service => service.RecordLastLoginDate(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            return users.Object;
        });
        builder.Services.AddScoped<IPersonalSubscriptionQueryService>(_ =>
        {
            var subscriptions = new Mock<IPersonalSubscriptionQueryService>();
            subscriptions.Setup(service => service.EnsureDefaultPlanAccessAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            return subscriptions.Object;
        });
        builder.Services.AddScoped<IPromoCodeService>(_ => Mock.Of<IPromoCodeService>());
        builder.Services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();
        builder.Services.AddScoped<ISiteNotificationPublisher>(_ => Mock.Of<ISiteNotificationPublisher>());
        builder.Services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        builder.Services.AddScoped<IAccountDeletionService>(_ => Mock.Of<IAccountDeletionService>());
        builder.Services.AddScoped<IEmailCodeLoginService>(_ => Mock.Of<IEmailCodeLoginService>());
        builder.Services.AddSingleton<ISupportMessageService>(supportMessages.Object);
        builder.Services.AddSingleton(Mock.Of<IOrganizationQueryService>());
        builder.Services.AddSingleton(Mock.Of<ISupportAttachmentService>());
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Authentication,
                _ => authenticationPermitLimit.HasValue
                    ? RateLimitPartition.GetFixedWindowLimiter("test", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authenticationPermitLimit.Value,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("test"));
            options.AddPolicy(
                OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.EmailDelivery,
                _ => RateLimitPartition.GetNoLimiter("test"));
            options.AddPolicy(
                OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.ConversationSend,
                _ => RateLimitPartition.GetNoLimiter("test"));
            options.AddFixedWindowLimiter(
                OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.ContactForm,
                limiter =>
                {
                    limiter.PermitLimit = 5;
                    limiter.Window = TimeSpan.FromHours(1);
                });
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AuthController).Assembly);

        var app = builder.Build();
        app.UsePathBase(pathBase);
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<EmailConfirmationMiddleware>();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapGet("/test/current-user", async (HttpContext context, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(context.User);

            return user == null ? Results.Unauthorized() : Results.Text(user.Id);
        });
        await app.StartAsync();

        return new TestApp(app, emailMessages, supportMessages);
    }

    private static async Task SignInAsync(
        HttpClient client,
        ApplicationUser user,
        Dictionary<string, string> cookies,
        string pathBase = "")
    {
        using var loginPage = await AuthMvcAcceptanceTests.GetPageAsync(client, pathBase + "/auth/login", cookies);
        using var login = await AuthMvcAcceptanceTests.PostFormAsync(
            client,
            pathBase + "/auth/login",
            new Dictionary<string, string>
            {
                ["Email"] = user.Email!,
                ["Password"] = AuthMvcAcceptanceTests.Password,
                ["ReturnUrl"] = pathBase + "/personal/support"
            },
            cookies,
            loginPage.AntiforgeryToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider services,
        string name,
        bool emailConfirmed = true)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = emailConfirmed
        };
        var result = await manager.CreateAsync(user, AuthMvcAcceptanceTests.Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));

        return user;
    }

    private static async Task<(ApplicationUser Administrator, ApplicationUser Target)> CreateImpersonationUsersAsync(
        IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var role = new IdentityRole($"Impersonation-{Guid.NewGuid():N}");
        Assert.True((await roleManager.CreateAsync(role)).Succeeded);
        var administrator = AuthMvcAcceptanceTests.NewUser("impersonator");
        var target = AuthMvcAcceptanceTests.NewUser("impersonated");
        Assert.True((await userManager.CreateAsync(administrator, AuthMvcAcceptanceTests.Password)).Succeeded);
        Assert.True((await userManager.CreateAsync(target, AuthMvcAcceptanceTests.Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(administrator, role.Name!)).Succeeded);
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = role.Id,
            PermissionCode = AdminAuthorizationPolicies.ImpersonateUsers,
            CreatedByUserId = administrator.Id
        });
        await context.SaveChangesAsync();

        return (administrator, target);
    }

    private static ApplicationUser NewUser(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@example.test";

        return new ApplicationUser { Email = email, UserName = email, EmailConfirmed = true };
    }

    private static async Task<T> GenerateTokenAsync<T>(IServiceProvider services, Func<UserManager<ApplicationUser>, Task<T>> generate)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return await generate(manager);
    }

    private static async Task<bool> ReadUserAsync(
        IServiceProvider services,
        string userId,
        Func<ApplicationUser, bool> predicate)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByIdAsync(userId);

        return user != null && predicate(user);
    }

    private static async Task<PageResponse> GetPageAsync(HttpClient client, string path, Dictionary<string, string> cookies)
    {
        var response = await AuthMvcAcceptanceTests.SendAsync(client, HttpMethod.Get, path, cookies);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        return new PageResponse(response, match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        Dictionary<string, string> values,
        Dictionary<string, string> cookies,
        string antiforgeryToken)
    {
        values["__RequestVerificationToken"] = antiforgeryToken;
        return await AuthMvcAcceptanceTests.SendAsync(
            client,
            HttpMethod.Post,
            path,
            cookies,
            new FormUrlEncodedContent(values));
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        string json,
        Dictionary<string, string> cookies,
        string? antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        AuthMvcAcceptanceTests.AddRequestCookies(request, cookies);
        if (!string.IsNullOrEmpty(antiforgeryToken))
        {
            request.Headers.Add("RequestVerificationToken", antiforgeryToken);
        }

        var response = await client.SendAsync(request);
        AuthMvcAcceptanceTests.AddResponseCookies(response, cookies);

        return response;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        Dictionary<string, string> cookies,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        AuthMvcAcceptanceTests.AddRequestCookies(request, cookies);

        return AuthMvcAcceptanceTests.SendAndUpdateCookiesAsync(client, request, cookies);
    }

    private static async Task<HttpResponseMessage> SendAndUpdateCookiesAsync(
        HttpClient client,
        HttpRequestMessage request,
        Dictionary<string, string> cookies)
    {
        var response = await client.SendAsync(request);
        AuthMvcAcceptanceTests.AddResponseCookies(response, cookies);

        return response;
    }

    private static void AddRequestCookies(HttpRequestMessage request, IReadOnlyDictionary<string, string> cookies)
    {
        if (cookies.Count != 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }
    }

    private static void AddResponseCookies(HttpResponseMessage response, IDictionary<string, string> cookies)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';', 2)[0].Split('=', 2);
            if (pair.Length == 2)
            {
                if (header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || pair[1].Length == 0)
                {
                    cookies.Remove(pair[0]);
                }
                else
                {
                    cookies[pair[0]] = pair[1];
                }
            }
        }
    }

    private sealed record PageResponse(HttpResponseMessage Response, string AntiforgeryToken) : IDisposable
    {
        public void Dispose() => this.Response.Dispose();
    }

    private sealed record TestApp(
        WebApplication Host,
        RecordingEmailNotificationService EmailMessages,
        Mock<ISupportMessageService> SupportMessages) : IAsyncDisposable
    {
        public IServiceProvider Services => this.Host.Services;

        public HttpClient GetTestClient() => this.Host.GetTestClient();

        public ValueTask DisposeAsync() => this.Host.DisposeAsync();
    }

    private sealed record TokenLifespanState(TimeSpan Value);

    private sealed record QueuedTemplate(string Email, string Subject, string TemplateKey, JsonElement Data);

    private sealed class RecordingEmailNotificationService : IEmailNotificationService
    {
        private readonly ConcurrentQueue<QueuedTemplate> _messages = new();

        public bool ThrowOnSend { get; set; }

        public IReadOnlyCollection<QueuedTemplate> Messages => this._messages.ToArray();

        public Task QueueAsync(
            string email,
            string subject,
            string html,
            string idempotencyKey,
            Guid? campaignRecipientId = null,
            long? subscriptionReminderId = null) => Task.CompletedTask;

        public Task QueueTemplateAsync(
            string email,
            string subject,
            string templateKey,
            object data,
            string? idempotencyKey = null,
            long? subscriptionReminderId = null)
        {
            if (this.ThrowOnSend)
            {
                throw new InvalidOperationException("Почтовый сервис временно недоступен.");
            }

            this._messages.Enqueue(new QueuedTemplate(email, subject, templateKey, JsonSerializer.SerializeToElement(data)));
            return Task.CompletedTask;
        }
    }
}
