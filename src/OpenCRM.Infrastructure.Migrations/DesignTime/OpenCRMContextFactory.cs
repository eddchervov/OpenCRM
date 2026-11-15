using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Migrations.DesignTime;

/// <summary> Создает контекст БД для design-time команд EF Core. </summary>
public sealed class OpenCRMContextFactory : IDesignTimeDbContextFactory<OpenCRMContext>
{
    /// <summary> Создает экземпляр контекста БД для команд миграции. </summary>
    /// <param name="args">Аргументы design-time команды.</param>
    /// <returns>Настроенный экземпляр контекста БД.</returns>
    public OpenCRMContext CreateDbContext(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(ResolveWebAppBasePath())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true);

        if (environmentName == "Development")
        {
            configurationBuilder.AddUserSecrets<OpenCRMContextFactory>(optional: true);
        }

        var configuration = configurationBuilder.AddEnvironmentVariables().Build();

        var connectionString = configuration.GetConnectionString("OpenCRMConnection")
            ?? throw new InvalidOperationException("Connection string 'OpenCRMConnection' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<OpenCRMContext>();

        optionsBuilder.UseNpgsql(connectionString, options =>
        {
            options.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
        });

        return new OpenCRMContext(optionsBuilder.Options);
    }

    /// <summary> Определяет путь к каталогу web-приложения с appsettings. </summary>
    /// <returns>Абсолютный путь к каталогу OpenCRM.WebApp.</returns>
    private static string ResolveWebAppBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new List<string> { currentDirectory };
        var cursor = new DirectoryInfo(currentDirectory);

        while (cursor != null)
        {
            candidates.Add(Path.Combine(cursor.FullName, "src", "OpenCRM.WebApp"));
            candidates.Add(Path.Combine(cursor.FullName, "OpenCRM.WebApp"));
            cursor = cursor.Parent;
        }

        foreach (var candidate in candidates
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Не удалось найти каталог OpenCRM.WebApp с appsettings.json.");
    }
}
