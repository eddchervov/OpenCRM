using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payments.Service.Configuration;
using Payments.Service.Endpoints;
using Payments.Service.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "brand.json"), optional: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
var brand = Helpers.ProjectBrand.Read(key => builder.Configuration[key]);
builder.Services.AddSingleton(brand);
builder.Configuration["Payments:AllowedReturnUrls:0"] = brand.PublicBaseUrl + "/payment/complete";
builder.Configuration["Payments:AllowedReturnUrls:1"] = brand.PublicBaseUrl + "/payment/mobile-complete";
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var migrationsOnly = args.Contains("--migrate", StringComparer.Ordinal);
builder.Services.AddPayments(builder.Configuration, migrationsOnly);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddRateLimiter(options => options.AddPolicy("webhooks", context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
        new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

await using var application = builder.Build();
if (migrationsOnly)
{
    await using var scope = application.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();

    return;
}

application.UseRateLimiter();
application.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
application.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable }
});
application.MapPaymentEndpoints();
await application.RunAsync();

/// <summary> Предоставляет точку входа интеграционным тестам HTTP-хоста. </summary>
public partial class Program;
