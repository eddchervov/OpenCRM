using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Service.Persistence;

public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentsDb")
            ?? throw new InvalidOperationException("Задайте ConnectionStrings__PaymentsDb для миграций Payments.");

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments"))
            .Options;

        return new PaymentDbContext(options);
    }
}
