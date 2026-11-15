using OpenCRM.Services.Interfaces.Documents.Dtos;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Word;
using OpenCRM.WebApp.Controllers.Api.V1.Documents;

namespace OpenCRM.Modules.Documents.Tests.Integration;

public sealed class DocumentHttpTests
{
    private static readonly Guid s_vehicleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_maintenanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_organizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_templateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly byte[] s_fileContent = [0, 1, 2, 3, 4, 255];

    public static TheoryData<string, string, string> Endpoints => new()
    {
        { "api/v1/documents/pdf/vehicle/" + s_vehicleId, "GenerateVehiclePdf", "application/pdf" },
        { "api/v1/documents/pdf/maintenance/" + s_maintenanceId, "GenerateMaintenancePdf", "application/pdf" },
        { "api/v1/maintenance/" + s_maintenanceId + "/intake/print?organizationId=" + s_organizationId, "PrintMaintenanceIntake", "application/pdf" },
        { "api/v1/documents/pdf/maintenance/" + s_maintenanceId + "/work-act?organizationId=" + s_organizationId, "GenerateMaintenanceWorkActPdf", "application/pdf" },
        { "api/v1/documents/pdf/maintenance/" + s_maintenanceId + "/work-order?organizationId=" + s_organizationId, "GenerateMaintenanceWorkOrderPdf", "application/pdf" },
        { "api/v1/documents/pdf/maintenance/" + s_maintenanceId + "/scenario/" + s_templateId + "?organizationId=" + s_organizationId, "GenerateScenarioMaintenanceDocumentPdf", "application/pdf" },
        { "api/v1/documents/word/vehicle/" + s_vehicleId, "GenerateVehicleWord", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "api/v1/documents/word/maintenance/" + s_maintenanceId, "GenerateMaintenanceWord", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "api/v1/documents/word/maintenance/" + s_maintenanceId + "/work-act?organizationId=" + s_organizationId, "GenerateMaintenanceWorkActWord", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "api/v1/documents/word/maintenance/" + s_maintenanceId + "/work-order?organizationId=" + s_organizationId, "GenerateMaintenanceWorkOrderWord", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "api/v1/documents/word/maintenance/" + s_maintenanceId + "/scenario/" + s_templateId + "?organizationId=" + s_organizationId, "GenerateScenarioMaintenanceDocumentWord", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Document_routes_require_authentication_and_map_export_results(
        string path,
        string expectedMethod,
        string expectedContentType)
    {
        var pdfExport = new FakePdfDocumentExportService();
        var wordExport = new FakeWordDocumentExportService();
        using var host = await CreateHostAsync(pdfExport, wordExport);
        var server = host.GetTestServer();
        using var client = server.CreateClient();
        var requestPath = $"/{path}{(path.Contains('?') ? '&' : '?')}includePhotos=true";
        if (!path.Contains("organizationId=", StringComparison.OrdinalIgnoreCase))
        {
            requestPath += $"&organizationId={s_organizationId}";
        }

        using var anonymousResponse = await client.GetAsync(requestPath);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Null(pdfExport.LastMethod);
        Assert.Null(wordExport.LastMethod);

        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        foreach (var outcome in new[]
        {
            DocumentExportResult.AccessDenied("Нет доступа к документу."),
            DocumentExportResult.NotFound("Документ не найден."),
            DocumentExportResult.InvalidRequest("Некорректные данные документа.")
        })
        {
            SetResult(pdfExport, wordExport, outcome);
            using var response = await client.GetAsync(requestPath);
            var expectedStatus = outcome.Outcome switch
            {
                DocumentExportOutcome.AccessDenied => HttpStatusCode.Forbidden,
                DocumentExportOutcome.NotFound => HttpStatusCode.NotFound,
                DocumentExportOutcome.InvalidRequest => HttpStatusCode.BadRequest,
                _ => throw new InvalidOperationException("Неожиданный результат теста.")
            };

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(expectedMethod, pdfExport.LastMethod ?? wordExport.LastMethod);
            Assert.Equal(
                expectedMethod.Contains("Vehicle", StringComparison.Ordinal) ? s_vehicleId : s_maintenanceId,
                pdfExport.LastEntityId ?? wordExport.LastEntityId);
            Assert.Equal(s_organizationId, pdfExport.LastOrganizationId ?? wordExport.LastOrganizationId);
        }

        SetResult(pdfExport, wordExport, DocumentExportResult.File(s_fileContent, expectedContentType, "maintenance-report.pdf"));
        using var fileResponse = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal(expectedContentType, fileResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(s_fileContent, await fileResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", fileResponse.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = fileResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? fileResponse.Content.Headers.ContentDisposition?.FileName;
        Assert.Contains("maintenance-report.pdf", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedMethod, pdfExport.LastMethod ?? wordExport.LastMethod);
        Assert.Equal("user-42", pdfExport.LastUserId ?? wordExport.LastUserId);
        Assert.Equal(
            expectedMethod.Contains("Vehicle", StringComparison.Ordinal) ? s_vehicleId : s_maintenanceId,
            pdfExport.LastEntityId ?? wordExport.LastEntityId);
        Assert.Equal(s_organizationId, pdfExport.LastOrganizationId ?? wordExport.LastOrganizationId);
        if (expectedMethod.Contains("Scenario", StringComparison.Ordinal))
        {
            Assert.Equal(s_templateId, pdfExport.LastTemplateId ?? wordExport.LastTemplateId);
        }

        if (expectedMethod is "GenerateVehiclePdf" or "GenerateMaintenancePdf" or "GenerateVehicleWord" or "GenerateMaintenanceWord")
        {
            Assert.True(pdfExport.LastIncludePhotos ?? wordExport.LastIncludePhotos);
            using var withoutPhotos = await client.GetAsync(requestPath.Replace("includePhotos=true", "includePhotos=false", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.OK, withoutPhotos.StatusCode);
            Assert.False(pdfExport.LastIncludePhotos ?? wordExport.LastIncludePhotos);
            using var defaultPhotos = await client.GetAsync(requestPath.Replace("includePhotos=true", "", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.OK, defaultPhotos.StatusCode);
            Assert.False(pdfExport.LastIncludePhotos ?? wordExport.LastIncludePhotos);
        }
    }

    [Theory]
    [InlineData("pdf", "vehicle", "GenerateVehiclePdf")]
    [InlineData("pdf", "maintenance", "GenerateMaintenancePdf")]
    [InlineData("word", "vehicle", "GenerateVehicleWord")]
    [InlineData("word", "maintenance", "GenerateMaintenanceWord")]
    public async Task PersonalRoutesPreserveAbsentOrganizationAndDefaultPhotoFlag(string format, string kind, string method)
    {
        var pdfExport = new FakePdfDocumentExportService();
        var wordExport = new FakeWordDocumentExportService();
        SetResult(pdfExport, wordExport, DocumentExportResult.File(s_fileContent, "application/octet-stream", "report"));
        using var host = await CreateHostAsync(pdfExport, wordExport);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        var entityId = kind == "vehicle" ? s_vehicleId : s_maintenanceId;

        using var response = await client.GetAsync($"/api/v1/documents/{format}/{kind}/{entityId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(method, pdfExport.LastMethod ?? wordExport.LastMethod);
        Assert.Null(pdfExport.LastOrganizationId);
        Assert.Null(wordExport.LastOrganizationId);
        Assert.False(pdfExport.LastIncludePhotos ?? wordExport.LastIncludePhotos);
        Assert.Equal("user-42", pdfExport.LastUserId ?? wordExport.LastUserId);
    }

    private static void SetResult(
        FakePdfDocumentExportService pdfExport,
        FakeWordDocumentExportService wordExport,
        DocumentExportResult result)
    {
        pdfExport.Result = result;
        wordExport.Result = result;
        pdfExport.LastMethod = null;
        pdfExport.LastUserId = null;
        pdfExport.LastEntityId = null;
        pdfExport.LastOrganizationId = null;
        wordExport.LastMethod = null;
        wordExport.LastUserId = null;
        wordExport.LastEntityId = null;
        wordExport.LastOrganizationId = null;
    }

    private static async Task<IHost> CreateHostAsync(
        FakePdfDocumentExportService pdfExport,
        FakeWordDocumentExportService wordExport)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        userManager.Setup(service => service.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns("user-42");

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddAuthentication("DocumentTest")
                        .AddScheme<AuthenticationSchemeOptions, DocumentTestAuthenticationHandler>("DocumentTest", _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(PdfController).Assembly);
                    services.AddSingleton(userManager.Object);
                    services.AddSingleton<IPdfDocumentExportService>(pdfExport);
                    services.AddSingleton<IWordDocumentExportService>(wordExport);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        return await hostBuilder.StartAsync();
    }

    private sealed class DocumentTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue("X-Test-Authenticated", out var value) || value != "true")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "user-42") };
            var identity = new ClaimsIdentity(claims, this.Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakePdfDocumentExportService : IPdfDocumentExportService
    {
        public DocumentExportResult Result { get; set; } = DocumentExportResult.AccessDenied("Нет доступа.");
        public string? LastMethod { get; set; }
        public string? LastUserId { get; set; }
        public Guid? LastEntityId { get; set; }
        public Guid? LastOrganizationId { get; set; }
        public bool? LastIncludePhotos { get; set; }
        public Guid? LastTemplateId { get; set; }

        public Task<DocumentExportResult> GenerateVehiclePdf(string userId, Guid vehicleId, bool includePhotos = false, Guid? organizationId = null) => this.Record(nameof(this.GenerateVehiclePdf), userId, vehicleId, organizationId, includePhotos);
        public Task<DocumentExportResult> GenerateMaintenancePdf(string userId, Guid maintenanceId, bool includePhotos = false, Guid? organizationId = null) => this.Record(nameof(this.GenerateMaintenancePdf), userId, maintenanceId, organizationId, includePhotos);
        public Task<DocumentExportResult> PrintMaintenanceIntake(string userId, Guid maintenanceId, Guid organizationId) => this.Record(nameof(this.PrintMaintenanceIntake), userId, maintenanceId, organizationId);
        public Task<DocumentExportResult> GenerateMaintenanceWorkActPdf(string userId, Guid maintenanceId, Guid organizationId) => this.Record(nameof(this.GenerateMaintenanceWorkActPdf), userId, maintenanceId, organizationId);
        public Task<DocumentExportResult> GenerateMaintenanceWorkOrderPdf(string userId, Guid maintenanceId, Guid organizationId) => this.Record(nameof(this.GenerateMaintenanceWorkOrderPdf), userId, maintenanceId, organizationId);
        public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentPdf(string userId, Guid maintenanceId, Guid templateId, Guid organizationId) => this.Record(nameof(this.GenerateScenarioMaintenanceDocumentPdf), userId, maintenanceId, organizationId, templateId: templateId);

        private Task<DocumentExportResult> Record(string method, string userId, Guid entityId, Guid? organizationId, bool? includePhotos = null, Guid? templateId = null)
        {
            this.LastMethod = method;
            this.LastUserId = userId;
            this.LastEntityId = entityId;
            this.LastOrganizationId = organizationId;
            this.LastIncludePhotos = includePhotos;
            this.LastTemplateId = templateId;

            return Task.FromResult(this.Result);
        }
    }

    private sealed class FakeWordDocumentExportService : IWordDocumentExportService
    {
        public DocumentExportResult Result { get; set; } = DocumentExportResult.AccessDenied("Нет доступа.");
        public string? LastMethod { get; set; }
        public string? LastUserId { get; set; }
        public Guid? LastEntityId { get; set; }
        public Guid? LastOrganizationId { get; set; }
        public bool? LastIncludePhotos { get; set; }
        public Guid? LastTemplateId { get; set; }

        public Task<DocumentExportResult> GenerateVehicleWord(string userId, Guid vehicleId, bool includePhotos = false, Guid? organizationId = null) => this.Record(nameof(this.GenerateVehicleWord), userId, vehicleId, organizationId, includePhotos);
        public Task<DocumentExportResult> GenerateMaintenanceWord(string userId, Guid maintenanceId, bool includePhotos = false, Guid? organizationId = null) => this.Record(nameof(this.GenerateMaintenanceWord), userId, maintenanceId, organizationId, includePhotos);
        public Task<DocumentExportResult> GenerateMaintenanceWorkActWord(string userId, Guid maintenanceId, Guid organizationId) => this.Record(nameof(this.GenerateMaintenanceWorkActWord), userId, maintenanceId, organizationId);
        public Task<DocumentExportResult> GenerateMaintenanceWorkOrderWord(string userId, Guid maintenanceId, Guid organizationId) => this.Record(nameof(this.GenerateMaintenanceWorkOrderWord), userId, maintenanceId, organizationId);
        public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentWord(string userId, Guid maintenanceId, Guid templateId, Guid organizationId) => this.Record(nameof(this.GenerateScenarioMaintenanceDocumentWord), userId, maintenanceId, organizationId, templateId: templateId);

        private Task<DocumentExportResult> Record(string method, string userId, Guid entityId, Guid? organizationId, bool? includePhotos = null, Guid? templateId = null)
        {
            this.LastMethod = method;
            this.LastUserId = userId;
            this.LastEntityId = entityId;
            this.LastOrganizationId = organizationId;
            this.LastIncludePhotos = includePhotos;
            this.LastTemplateId = templateId;

            return Task.FromResult(this.Result);
        }
    }
}
