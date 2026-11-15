using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Extensions;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(false, "12345678", "PasswordRequiresLetter")]
    [InlineData(true, "12345678", "PasswordRequiresLetter")]
    [InlineData(false, "abcdefgh", "PasswordRequiresDigit")]
    [InlineData(true, "abcdefgh", "PasswordRequiresDigit")]
    [InlineData(false, "Abc1234", "PasswordTooShort")]
    [InlineData(true, "Abc1234", "PasswordTooShort")]
    public async Task EffectivePolicyRejectsWeakPasswords(bool development, string password, string errorCode)
    {
        using var services = PasswordPolicyTests.CreateServices(development);
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await manager.CreateAsync(PasswordPolicyTests.CreateUser(), password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == errorCode);
    }

    [Theory]
    [InlineData(false, "abcdefg1")]
    [InlineData(true, "abcdefg1")]
    [InlineData(false, "ABCDEFG1")]
    [InlineData(true, "ABCDEFG1")]
    [InlineData(false, "пароль12")]
    [InlineData(true, "пароль12")]
    [InlineData(false, "a1111111")]
    [InlineData(true, "a1111111")]
    public async Task EffectivePolicyAcceptsEightCharactersWithLetterAndDigit(bool development, string password)
    {
        using var services = PasswordPolicyTests.CreateServices(development);
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = PasswordPolicyTests.CreateUser();

        var result = await manager.CreateAsync(user, password);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        Assert.True(await manager.CheckPasswordAsync(user, password));
    }

    [Fact]
    public async Task SetChangeAndResetEnforcePolicyWhileLegacyPasswordStillAuthenticates()
    {
        using var services = PasswordPolicyTests.CreateServices(false);
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = PasswordPolicyTests.CreateUser();
        Assert.True((await manager.CreateAsync(user)).Succeeded);

        var invalidSet = await manager.AddPasswordAsync(user, "12345678");
        Assert.Contains(invalidSet.Errors, error => error.Code == "PasswordRequiresLetter");
        Assert.False(await manager.HasPasswordAsync(user));
        Assert.True((await manager.AddPasswordAsync(user, "abcdefg1")).Succeeded);

        user.PasswordHash = manager.PasswordHasher.HashPassword(user, "12345678");
        Assert.True(await manager.CheckPasswordAsync(user, "12345678"));

        var invalidChange = await manager.ChangePasswordAsync(user, "12345678", "87654321");
        Assert.Contains(invalidChange.Errors, error => error.Code == "PasswordRequiresLetter");
        Assert.True(await manager.CheckPasswordAsync(user, "12345678"));
        Assert.True((await manager.ChangePasswordAsync(user, "12345678", "ABCDEFG1")).Succeeded);

        var token = await manager.GeneratePasswordResetTokenAsync(user);
        var invalidReset = await manager.ResetPasswordAsync(user, token, "abcdefgh");
        Assert.Contains(invalidReset.Errors, error => error.Code == "PasswordRequiresDigit");
        Assert.True(await manager.CheckPasswordAsync(user, "ABCDEFG1"));
        Assert.True((await manager.ResetPasswordAsync(user, token, "пароль12")).Succeeded);
        Assert.True(await manager.CheckPasswordAsync(user, "пароль12"));
        Assert.False(await manager.CheckPasswordAsync(user, "ABCDEFG1"));
    }

    private static ApplicationUser CreateUser() => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserName = $"password-policy-{Guid.NewGuid():N}@example.test",
        Email = $"password-policy-{Guid.NewGuid():N}@example.test"
    };

    private static ServiceProvider CreateServices(bool development, [CallerFilePath] string sourceFile = "")
    {
        var webRoot = Path.GetFullPath("../../../src/OpenCRM.WebApp", Path.GetDirectoryName(sourceFile)!);
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(webRoot)
            .AddJsonFile("appsettings.json");
        if (development)
        {
            configurationBuilder.AddJsonFile("appsettings.Development.json");
        }

        var store = new Mock<IUserPasswordStore<ApplicationUser>>();
        store.Setup(value => value.GetUserIdAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.Id));
        store.Setup(value => value.GetUserNameAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.UserName));
        store.Setup(value => value.GetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.PasswordHash));
        store.Setup(value => value.HasPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.PasswordHash != null));
        store.Setup(value => value.SetPasswordHashAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((ApplicationUser user, string? hash, CancellationToken _) => user.PasswordHash = hash)
            .Returns(Task.CompletedTask);
        store.Setup(value => value.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        store.Setup(value => value.UpdateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        var stampStore = store.As<IUserSecurityStampStore<ApplicationUser>>();
        stampStore.Setup(value => value.GetSecurityStampAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.SecurityStamp));
        stampStore.Setup(value => value.SetSecurityStampAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((ApplicationUser user, string stamp, CancellationToken _) => user.SecurityStamp = stamp)
            .Returns(Task.CompletedTask);

        var emailStore = store.As<IUserEmailStore<ApplicationUser>>();
        emailStore.Setup(value => value.GetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationUser user, CancellationToken _) => Task.FromResult(user.Email));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenCRMIdentity(configurationBuilder.Build());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IUserStore<ApplicationUser>>(store.Object);

        return services.BuildServiceProvider();
    }
}
