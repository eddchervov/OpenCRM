using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data.History;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class HistoryActorDisplayNameTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    public static TheoryData<bool, string?, string?, string?, string> ActorCases => new()
    {
        { false, "contact@example.invalid", "contact@example.invalid", "active-user", "contact@example.invalid" },
        { true, "contact@example.invalid", "contact@example.invalid", "deleted-user", "contact@example.invalid" },
        { false, " ", "contact@example.invalid", "user-name", "user-name" },
        { true, "contact@example.invalid", "\t", "user-name", "user-name" },
        { false, null, null, null, "Пользователь" },
        { true, "contact@example.invalid", null, " ", "Пользователь" },
        { false, "  contact@example.invalid  ", null, null, "  contact@example.invalid  " }
    };

    [Theory]
    [MemberData(nameof(ActorCases))]
    public async Task ResolveAsyncUsesExistingActorFallbackRules(
        bool isDeleted,
        string? email,
        string? originalEmail,
        string? userName,
        string expected)
    {
        var userId = Guid.NewGuid().ToString();
        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                Email = email,
                OriginalEmail = originalEmail,
                UserName = userName,
                IsDeleted = isDeleted
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var displayName = await HistoryActorDisplayNameQuery.ResolveAsync(context, userId);

        Assert.Equal(expected, displayName);
        Assert.Empty(context.ChangeTracker.Entries<ApplicationUser>());
    }

    [Fact]
    public async Task ResolveAsyncReturnsUnknownForMissingActor()
    {
        await using var context = fixture.CreateContext();

        var displayName = await HistoryActorDisplayNameQuery.ResolveAsync(context, Guid.NewGuid().ToString());

        Assert.Equal("Неизвестный пользователь", displayName);
        Assert.Empty(context.ChangeTracker.Entries<ApplicationUser>());
    }
}
