using OpenCRM.Hosting;

namespace OpenCRM.WebApp;

/// <summary> Запускает самостоятельную нейтральную CRM через общую композицию приложения. </summary>
public sealed class Program
{
    /// <summary> Настраивает и запускает веб-приложение. </summary>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        OpenCRMApplication.ConfigureBuilder(builder, args);
        var app = builder.Build();
        OpenCRMApplication.ConfigureApplication(app);
        app.Run();
    }
}
