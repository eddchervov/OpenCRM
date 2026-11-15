using OpenCRM.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using Npgsql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class RegistrationTransactionTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatesUserWithRetryEnabledInsideTransaction(bool withPassword)
    {
        await using var context = fixture.CreateContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"registration-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            OriginalEmail = email,
            DatetimeCreation = DateTime.UtcNow,
            EmailConfirmed = !withPassword
        };

        await new ApplicationUnitOfWork(context).ExecuteInTransactionAsync(async () =>
        {
            var result = withPassword
                ? await manager.CreateAsync(user, "Registration-test-123!")
                : await manager.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        });

        await using var verification = fixture.CreateContext();
        var saved = await verification.Users.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(email.ToUpperInvariant(), saved.NormalizedEmail);
        Assert.Equal(email.ToUpperInvariant(), saved.NormalizedUserName);
        Assert.Equal(!withPassword, saved.EmailConfirmed);
        Assert.Equal(withPassword, saved.PasswordHash != null);
        if (withPassword)
        {
            Assert.True(await manager.CheckPasswordAsync(saved, "Registration-test-123!"));
        }
    }

    [Fact]
    public async Task RetriesWholeTransactionAfterTransientFailureWithoutKeepingRolledBackEntities()
    {
        await using var context = fixture.CreateContext();
        var email = $"retry-{Guid.NewGuid():N}@example.com";
        var attemptCount = 0;

        await new ApplicationUnitOfWork(context).ExecuteInTransactionAsync(async () =>
        {
            attemptCount++;
            context.Users.Add(new ApplicationUser { Email = email, UserName = email });
            await context.SaveChangesAsync();

            if (attemptCount == 1)
            {
                // Несохранённая сущность также должна исчезнуть после отката.
                context.Users.Add(new ApplicationUser { Email = "discard-" + email, UserName ="discard-" + email });

                throw new NpgsqlException("Временный сбой базы данных.", new TimeoutException());
            }
        });

        Assert.Equal(2, attemptCount);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.Users.CountAsync(user => user.Email == email));
        Assert.False(await verification.Users.AnyAsync(user => user.Email == "discard-" + email));
    }

    [Fact]
    public async Task RollsBackUserWhenFollowingRegistrationStepFails()
    {
        await using var context = fixture.CreateContext();
        var email = $"rollback-{Guid.NewGuid():N}@example.com";
        var unitOfWork = new ApplicationUnitOfWork(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                context.Users.Add(new ApplicationUser { Email = email, UserName = email });
                await context.SaveChangesAsync();

                throw new InvalidOperationException("Ошибка сохранения профиля.");
            }));

        Assert.Equal("Ошибка сохранения профиля.", error.Message);
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Users.AnyAsync(user => user.Email == email));
    }
}
