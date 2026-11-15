using OpenCRM.WebApp.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenCRM.WebApp.Infrastructure.Health;
using System.IO.Compression;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Hangfire;
using Hangfire.Dashboard;
using OpenCRM.WebApp.Infrastructure;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.WebApp.Infrastructure.Hangfire;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Infrastructure.UserActivity;
using Serilog;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

namespace OpenCRM.Hosting;

/// <summary> Общая регистрация сервисов и HTTP-конвейера CRM для самостоятельного приложения и продуктов. </summary>
public static class OpenCRMApplication
{
    private const string ApiCorsPolicyName = "OpenCRMApiCors";

    /// <summary> Настраивает общие сервисы до добавления расширений конкретного продукта. </summary>
    public static void ConfigureBuilder(WebApplicationBuilder builder, string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(args);
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "brand.json"), optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        var brand = Helpers.ProjectBrand.Read(key => builder.Configuration[key]);
        builder.Services.AddSingleton(brand);
        builder.Configuration["AppSettings:BaseUrl"] = brand.PublicBaseUrl;
        builder.Configuration["YooKassa:ReturnUrl"] = brand.PublicBaseUrl + "/payment/complete";
        builder.Configuration["PaymentsService:ReturnUrl"] = brand.PublicBaseUrl + "/payment/complete";
        builder.Configuration["PaymentsService:AllowedReturnUrls:0"] = brand.PublicBaseUrl + "/payment/complete";
        builder.Configuration["PaymentsService:AllowedReturnUrls:1"] = brand.PublicBaseUrl + "/payment/mobile-complete";
        var knownProxyAddress = builder.Configuration["ReverseProxy:KnownProxy"];
        var knownProxy = IPAddress.Loopback;

        if (!string.IsNullOrWhiteSpace(knownProxyAddress) && !IPAddress.TryParse(knownProxyAddress, out knownProxy))
        {
            throw new InvalidOperationException("ReverseProxy:KnownProxy должен содержать IP-адрес доверенного прокси.");
        }

        if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(knownProxyAddress))
        {
            throw new InvalidOperationException("Для Production необходимо задать ReverseProxy:KnownProxy.");
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;

            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(knownProxy);
        });

        builder.Services.Configure<RouteOptions>(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = false;
        });

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "application/xml",
                "image/svg+xml",
                "font/woff2"
            ]);
        });

        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(OpenCRMApplication.ApiCorsPolicyName, policy =>
            {
                var allowedOrigins = builder.Configuration
                    .GetSection("Security:AllowedCorsOrigins")
                    .Get<string[]>()
                    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? [];

                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OpenCRM.Hosting");
                logger.LogWarning(
                    "Rate limit rejected {Path} from {ClientIp}.",
                    context.HttpContext.Request.Path,
                    context.HttpContext.Connection.RemoteIpAddress);
                if (context.HttpContext.Request.Path.Equals($"{EmailConfirmationMiddleware.ConfirmationPath}/resend"))
                {
                    var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                        : 600;

                    var retryAt = DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds).ToUnixTimeSeconds();
                    context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    context.HttpContext.Response.Redirect($"{context.HttpContext.Request.PathBase}{EmailConfirmationMiddleware.ConfirmationPath}?retryAt={retryAt}");

                    return;
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        title = "Слишком много запросов",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Повторите попытку немного позже."
                    },
                    cancellationToken);
            };

            options.AddPolicy(
                RateLimitPolicyNames.Authentication,
                context => OpenCRMApplication.CreateFixedWindowLimiter(context, permitLimit: 20, window: TimeSpan.FromMinutes(5)));
            options.AddPolicy(
                RateLimitPolicyNames.EmailDelivery,
                context => OpenCRMApplication.CreateFixedWindowLimiter(context, permitLimit: 3, window: TimeSpan.FromMinutes(10)));
            options.AddPolicy(
                RateLimitPolicyNames.ContactForm,
                context => OpenCRMApplication.CreateFixedWindowLimiter(context, permitLimit: 5, window: TimeSpan.FromHours(1), preferUserId: true));
            options.AddPolicy(
                RateLimitPolicyNames.Payments,
                context => OpenCRMApplication.CreateFixedWindowLimiter(context, permitLimit: 10, window: TimeSpan.FromMinutes(1), preferUserId: true));
            options.AddPolicy(
                RateLimitPolicyNames.ConversationSend,
                context => OpenCRMApplication.CreateFixedWindowLimiter(context, permitLimit: 30, window: TimeSpan.FromMinutes(1), preferUserId: true));
            options.AddPolicy(
                RateLimitPolicyNames.ConversationFiles,
                _ => RateLimitPartition.GetConcurrencyLimiter("conversation-files",
                    _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }));
            options.AddPolicy(
                RateLimitPolicyNames.ConversationEvents,
                context => RateLimitPartition.GetConcurrencyLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new ConcurrencyLimiterOptions { PermitLimit = 3, QueueLimit = 0 }));
            options.AddPolicy(
                RateLimitPolicyNames.OrganizationDataExport,
                context => RateLimitPartition.GetConcurrencyLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }));
        });

        builder.Services
            .AddOpenCRMDataAccess(builder.Configuration)
            .AddOpenCRMIdentity(builder.Configuration)
            .AddOpenCRMPresentation(brand)
            .AddOpenCRMInfrastructure(builder.Configuration)
            .AddOpenCRMBackgroundJobs(builder.Configuration);

        builder.Services.AddServerAssistant(builder.Configuration);
        builder.Services.AddAuthorization(options => options.AddPolicy("AiPilotLocalSession", policy =>
            policy.AddAuthenticationSchemes("Identity.Application").RequireAuthenticatedUser()));

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>("database", timeout: TimeSpan.FromSeconds(10));

        // Адрес прослушивания задаётся настройкой или ASPNETCORE_URLS: базовая
        // поставка не привязывает приложение ко всем интерфейсам по умолчанию.
        var applicationUrl = builder.Configuration["LocalTesting:ApplicationUrl"];
        if (!string.IsNullOrWhiteSpace(applicationUrl))
        {
            builder.WebHost.UseUrls(applicationUrl);
        }
        OpenCRMApplication.ConfigureLogging(builder.Configuration);
        builder.Host.UseSerilog();
    }

    /// <summary> Подключает общий HTTP-конвейер к приложению с уже зарегистрированными расширениями. </summary>
    public static void ConfigureApplication(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var brand = app.Services.GetRequiredService<Helpers.ProjectBrand>();

        app.UseForwardedHeaders();
        app.UseMiddleware<ApiExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/api"),
                branch => branch.UseDeveloperExceptionPage());
        }
        else
        {
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/api"),
                branch => branch.UseExceptionHandler("/error/500"));
            app.UseHsts();
        }

        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));

        app.MapFallbackToController(
            action: "HandleError",
            controller: "Error");

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseResponseCompression();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            var traceId = context.TraceIdentifier;
            var isLogout = string.Equals(context.Request.Path.Value, "/auth/logout", StringComparison.OrdinalIgnoreCase);
            context.Response.Headers["X-Request-Id"] = traceId;

            try
            {
                await next();

                var statusCode = context.Response.StatusCode;
                var controller = context.Request.RouteValues["controller"]?.ToString();
                var action = context.Request.RouteValues["action"]?.ToString();
                var userId = context.User.FindFirstValue("UserID")
                    ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (statusCode >= StatusCodes.Status500InternalServerError)
                {
                    app.Logger.LogError(
                        "HTTP-запрос завершился с ошибкой сервера. Method: {Method}, StatusCode: {StatusCode}, Controller: {Controller}, Action: {Action}, UserId: {UserId}, TraceId: {TraceId}",
                        context.Request.Method, statusCode, controller, action, userId, traceId);
                }
                else if (statusCode >= StatusCodes.Status400BadRequest && (context.GetEndpoint() != null || isLogout))
                {
                    app.Logger.LogWarning(
                        "HTTP-запрос отклонён. Method: {Method}, StatusCode: {StatusCode}, Controller: {Controller}, Action: {Action}, UserId: {UserId}, TraceId: {TraceId}",
                        context.Request.Method, statusCode, controller, action, userId, traceId);
                }
                else if (isLogout)
                {
                    app.Logger.LogInformation("Выход выполнен. UserId: {UserId}, TraceId: {TraceId}", userId, traceId);
                }
            }
            catch (Exception exception)
            {
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    app.Logger.LogError(exception,
                        "Необработанная ошибка HTTP-запроса. Method: {Method}, Controller: {Controller}, Action: {Action}, TraceId: {TraceId}",
                        context.Request.Method, context.Request.RouteValues["controller"],
                        context.Request.RouteValues["action"], traceId);
                }

                throw;
            }
        });
        app.UseCors(OpenCRMApplication.ApiCorsPolicyName);
        app.UseCookiePolicy();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<EmailConfirmationMiddleware>();
        app.UseAuthorization();

        app.UseMiddleware<UserActivityMiddleware>();
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/admin-panel"),
            branch => branch.UseMiddleware<AdminPanelAuthorizationMiddleware>());
        app.UseStaticFiles(OpenCRMApplication.ConfigureStaticFileCaching());

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", $"{brand.DisplayName} API v1");
                options.RoutePrefix = "swagger";
            });
        }

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization =
            [
                new HangfireDashboardAuthorizationFilter()
            ],
            DashboardTitle = $"{brand.DisplayName} background jobs"
        });

        app.MapStaticAssets().WithMetadata(new AllowUnconfirmedEmailAttribute());

        app.MapHealthChecks("/api/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous().WithMetadata(new AllowUnconfirmedEmailAttribute());

        app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable
            }
        }).AllowAnonymous().WithMetadata(new AllowUnconfirmedEmailAttribute());

        app.MapControllers();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        app.MapRazorPages()
            .WithStaticAssets();
    }

    /// <summary> Создаёт ограничитель частоты запросов с фиксированным окном для контекста запроса. </summary>
    private static RateLimitPartition<string> CreateFixedWindowLimiter(
        HttpContext context,
        int permitLimit,
        TimeSpan window,
        bool preferUserId = false)
    {
        var userId = preferUserId
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        var clientKey = !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            clientKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
    }

    /// <summary> Настраивает логирование по параметрам конфигурации. </summary>
    private static void ConfigureLogging(IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("Application", "OpenCRM.WebApp")
            .CreateLogger();

        Log.Information("OpenCRM.WebApp logging configured");
    }

    /// <summary> Настраивает параметры выдачи статических файлов с кэшированием. </summary>
    private static StaticFileOptions ConfigureStaticFileCaching()
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".epf"] = "application/octet-stream";

        return new StaticFileOptions
        {
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = context =>
            {
                var path = context.File.PhysicalPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var fileName = Path.GetFileName(path);
                if (string.Equals(fileName, "OpenCRMAccountingImport.epf", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                    return;
                }

                if (OpenCRMApplication.ShouldRevalidateClientAsset(path))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                    return;
                }

                context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            }
        };
    }

    /// <summary> Определяет, требуется ли ревалидация клиентского ресурса по пути. </summary>
    private static bool ShouldRevalidateClientAsset(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.Contains("/wwwroot/admin-panel/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        if (!normalizedPath.Contains("/wwwroot/clients/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (fileName is "index.html" or "manifest.json")
        {
            return true;
        }

        return (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            && !Regex.IsMatch(fileName, "-[a-f0-9]{8}\\.", RegexOptions.CultureInvariant);
    }
}
