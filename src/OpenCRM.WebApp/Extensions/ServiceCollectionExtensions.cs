using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Promotions;
using OpenCRM.Modules.Planning;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Workspace;
using OpenCRM.Modules.Imports;
using OpenCRM.Modules.Accounts;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Support;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Communications;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Maintenance;
using Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Documents;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Auth;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Users;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.WebApp.Infrastructure.Hangfire;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Infrastructure.UserActivity;
using OpenCRM.WebApp.Infrastructure.Swagger;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace OpenCRM.WebApp.Extensions;

/// <summary> Методы регистрации сервисов приложения в DI-контейнере. </summary>
public static class ServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы доступа к данным. </summary>
    public static IServiceCollection AddOpenCRMDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OpenCRMConnection")
            ?? throw new InvalidOperationException("Connection string 'OpenCRMConnection' not found.");

        services.AddDbContext<OpenCRMContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptionsAction: option =>
            {
                option.MigrationsAssembly(typeof(AssemblyMarker).GetTypeInfo().Assembly.GetName().Name);
                option.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorCodesToAdd: null);
            }));

        return services;
    }

    /// <summary> Регистрирует фоновые задачи приложения. </summary>
    public static IServiceCollection AddOpenCRMBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OpenCRMConnection")
            ?? throw new InvalidOperationException("Connection string 'OpenCRMConnection' not found.");

        services.AddHangfire((provider, config) =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    options => options.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = configuration.GetValue<bool>("Hangfire:PrepareSchemaIfNecessary")
                    });
        });

        services.AddHangfireServer(options =>
        {
            options.ServerName = $"OpenCRM.WebApp:{Environment.MachineName}";
            options.WorkerCount = Math.Max(Environment.ProcessorCount, 2);
            options.Queues = ["default"];
        });
        services.AddHostedService<RecurringJobsRegistrationService>();
        services.AddHostedService<OpenCRM.WebApp.Infrastructure.Imports.OrganizationImportWorker>();
        services.AddSingleton(configuration.GetSection("OrganizationDataExport")
            .Get<OpenCRM.Modules.Organizations.Export.OrganizationDataExportOptions>() ?? new());
        services.AddHostedService<OpenCRM.WebApp.Infrastructure.Organizations.OrganizationDataExportWorker>();
        services.AddScoped<OpenCRM.WebApp.Infrastructure.Billing.BillingAutomationService>();

        return services;
    }

    /// <summary> Регистрирует сервисы Identity и аутентификации. </summary>
    public static IServiceCollection AddOpenCRMIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserAccessInfoAccessor, HttpUserAccessInfoAccessor>();

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<OpenCRMContext>()
            .AddErrorDescriber<OpenCRMIdentityErrorDescriber>()
            .AddPasswordValidator<LetterPasswordValidator>()
            .AddDefaultTokenProviders();

        services.Configure<IdentityOptions>(configuration.GetSection("IdentitySettings"));

        services.ConfigureApplicationCookie(options =>
        {
            options.AccessDeniedPath = "/error/404";
            options.LoginPath = "/auth/login";
            options.LogoutPath = "/auth/logout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            };
        });

        services.AddAuthentication()
            .AddJwtBearer(options =>
            {
                var jwtOptions = configuration.GetSection("Authentication:Jwt").Get<JwtTokenOptions>() ?? new JwtTokenOptions();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = AuthTokenService.CreateSecurityKey(jwtOptions.SigningKey),
                    ValidateLifetime = true,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is not ClaimsIdentity identity)
                        {
                            return Task.CompletedTask;
                        }

                        var userId = identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                            ?? identity.FindFirst("sub")?.Value
                            ?? identity.FindFirst("nameid")?.Value
                            ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                        if (!string.IsNullOrWhiteSpace(userId))
                        {
                            foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToList())
                            {
                                identity.RemoveClaim(claim);
                            }

                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                        }

                        return Task.CompletedTask;
                    }
                };
            });
        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(24);
        });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, BrowserAuthorizationMiddlewareResultHandler>();
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    IdentityConstants.ApplicationScheme,
                    JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    /// <summary> Регистрирует сервисы уровня представления. </summary>
    public static IServiceCollection AddOpenCRMPresentation(this IServiceCollection services, ProjectBrand brand)
    {
        // Бренд используется и через контейнер: например, middleware админ-панели получает его в конструкторе.
        services.AddSingleton(brand);

        services.AddWebEncoders(options =>
        {
            options.TextEncoderSettings = new TextEncoderSettings(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.Cyrillic,
                UnicodeRanges.GeneralPunctuation);
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "RequestVerificationToken";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        services.AddControllersWithViews(options =>
        {
            options.Filters.Add<ApiAntiforgeryAuthorizationFilter>();
            options.Filters.Add<ApiErrorResultFilter>();
        }).AddApplicationPart(typeof(OpenCRM.Hosting.OpenCRMApplication).Assembly);
        services.AddRazorPages().AddRazorRuntimeCompilation();
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<OpenCRM.WebApp.Models.Organizations.IOrganizationWikiContentProvider,
            OpenCRM.WebApp.Models.Organizations.EmptyOrganizationWikiContentProvider>();
        services.AddSingleton<OpenCRM.Services.Interfaces.Organizations.IOrganizationWikiAvailability,
            OpenCRM.WebApp.Models.Organizations.OrganizationWikiAvailability>();
        services.AddSingleton<EmailConfirmationDelivery>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                // Название берётся из общего источника бренда, как и подпись страницы Swagger.
                Title = $"{brand.DisplayName} API",
                Version = "v1",
                Description = "API для веб-приложения и клиентских приложений."
            });

            options.EnableAnnotations();
            options.OperationFilter<DefaultSwaggerOperationFilter>();

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Введите JWT access token без префикса Bearer."
            });

        });

        return services;
    }

    /// <summary> Регистрирует инфраструктурные сервисы приложения. </summary>
    public static IServiceCollection AddOpenCRMInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = 100 * 1024 * 1024;
        });

        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
        });

        var keysDirectory = configuration["DataProtection:KeysDirectory"];
        if (string.IsNullOrWhiteSpace(keysDirectory))
        {
            keysDirectory = "/app/keys";
        }

        if (!Directory.Exists(keysDirectory))
        {
            Directory.CreateDirectory(keysDirectory);
        }

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "OpenCRM.WebApp")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        services.AddScoped<IEmailCampaignScheduler, EmailCampaignScheduler>();
        services.AddOpenCRMInfrastructure();
        services.AddOpenCRMAdminModule();
        services.AddUserActivity(configuration);
        services.AddOpenCRMAuthInfrastructure(configuration);
        services.AddOpenCRMPaymentsInfrastructure(configuration);
        services.AddOpenCRMRepositories();
        services.AddOpenCRMDocuments();
        services.AddOpenCRMVehicles();
        services.AddOpenCRMInventory();
        services.AddOpenCRMMaintenance();
        services.AddOpenCRMClients();
        services.AddOpenCRMCommunications();
        services.AddOpenCRMCatalog();
        services.AddOpenCRMBilling();
        services.AddOpenCRMPromotions();
        services.AddOpenCRMPlanning();
        services.AddOpenCRMOrganizations();
        services.AddOpenCRMWorkspace();
        services.AddOpenCRMImports();
        services.AddOpenCRMAccounts();
        services.AddOpenCRMNotifications(configuration);
        services.AddOpenCRMSupport();
        services.AddOpenCRMFileStorageInfrastructure();

        return services;
    }
}
