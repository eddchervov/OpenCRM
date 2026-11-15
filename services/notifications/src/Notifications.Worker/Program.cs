using Notifications.Infrastructure.Providers.UnisenderGo;
using Notifications.Application;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Templating;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "brand.json"), optional: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
var brand = Helpers.ProjectBrand.Read(key => builder.Configuration[key]);
builder.Services.AddSingleton(brand);
var projectName = brand.DisplayName;
// Название бренда, подписи отправителя и метка писем берутся из общего источника в одном месте.
SystemTemplateBrandConfiguration.ApplyProjectBrand(builder.Configuration, projectName);

// JSON-логи пишутся в stdout, откуда их забирает Docker или внешний сборщик.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

var migrationsOnly = args.Contains("--migrate", StringComparer.Ordinal);
StartupConfigurationValidator.Validate(builder.Configuration, builder.Environment, migrationsOnly);
builder.Services.AddNotificationInfrastructure(builder.Configuration, migrationsOnly);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
await using var host = builder.Build();

// Применение миграций при старте управляется конфигурацией окружения.
if (migrationsOnly
    || host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:MigrateOnStartup"))
{
    await host.Services.ApplyNotificationMigrationsAsync();
}

if (migrationsOnly)
{
    return;
}

host.MapNotificationWebhooks();
host.MapNotificationHealthChecks();

// Процесс остаётся активным до штатной остановки хоста и корректно передаёт cancellation token обработчикам.
await host.RunAsync();
