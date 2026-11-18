using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class PreferredUiCultureMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/admin/users", "zh-Hans", "ru", "zh-Hans")]
    [InlineData("/api/v1/admin/users", "en", "zh-Hans", "en-US")]
    [InlineData("/api/v1/admin/users", "unsupported", "ru", "ru-RU")]
    [InlineData("/api/v1/admin/users", "en,zh-Hans", "ru", "ru-RU")]
    [InlineData("/admin-panel", "zh-Hans", "ru", "ru-RU")]
    public async Task ExplicitApiLanguageOverridesAccountLanguageWithoutChangingFormats(
        string path,
        string header,
        string preferredLanguage,
        string expectedUiCulture)
    {
        var user = new ApplicationUser
        {
            Id = "fictional-admin",
            PreferredLanguage = preferredLanguage,
            PreferredFormattingCulture = "en-GB",
            PersonalCurrencyCode = "CNY"
        };

        using var services = PreferredUiCultureMiddlewareTests.CreateServices(user);
        var context = PreferredUiCultureMiddlewareTests.CreateContext(path, header);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var reachedNext = false;
        var middleware = new PreferredUiCultureMiddleware(current =>
        {
            reachedNext = true;
            Assert.Equal(expectedUiCulture, CultureInfo.CurrentUICulture.Name);
            Assert.Equal("en-GB", CultureInfo.CurrentCulture.Name);
            Assert.Equal("CNY", current.Items[SupportedUiLanguages.PersonalCurrencyItemKey]);

            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, services.GetRequiredService<UserManager<ApplicationUser>>());

        Assert.True(reachedNext);
        Assert.Equal(preferredLanguage, user.PreferredLanguage);
        Assert.Equal("en-GB", user.PreferredFormattingCulture);
        Assert.Equal(expectedUiCulture, context.Items[SupportedUiLanguages.RequestCultureItemKey]);
        Assert.Equal(originalCulture, CultureInfo.CurrentCulture);
        Assert.Equal(originalUiCulture, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public async Task AnonymousApiLanguageIsAppliedAndRestoredWhenRequestFails()
    {
        using var services = PreferredUiCultureMiddlewareTests.CreateServices(null);
        var context = PreferredUiCultureMiddlewareTests.CreateContext("/api/v1/admin/auth/login", "zh-CN");
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var middleware = new PreferredUiCultureMiddleware(_ =>
        {
            Assert.Equal("zh-Hans", CultureInfo.CurrentUICulture.Name);
            Assert.Equal(originalCulture, CultureInfo.CurrentCulture);

            throw new InvalidOperationException("test failure");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(
            context, services.GetRequiredService<UserManager<ApplicationUser>>()));

        Assert.Equal("zh-Hans", context.Items[SupportedUiLanguages.RequestCultureItemKey]);
        Assert.Equal(originalCulture, CultureInfo.CurrentCulture);
        Assert.Equal(originalUiCulture, CultureInfo.CurrentUICulture);
    }

    private static DefaultHttpContext CreateContext(string path, string header)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Headers[SupportedUiLanguages.RequestLanguageHeader] = header;

        return context;
    }

    private static ServiceProvider CreateServices(ApplicationUser? user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>();
        services.AddSingleton<IUserStore<ApplicationUser>>(new ReadOnlyUserStore(user));

        return services.BuildServiceProvider();
    }

    private sealed class ReadOnlyUserStore(ApplicationUser? user) : IUserStore<ApplicationUser>
    {
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == userId ? user : null);

        public Task<string> GetUserIdAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.UserName);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.NormalizedUserName);

        public Task SetUserNameAsync(ApplicationUser value, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetNormalizedUserNameAsync(ApplicationUser value, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> CreateAsync(ApplicationUser value, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(ApplicationUser value, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(ApplicationUser value, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
