using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientCommentCommandTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task UpdatesOnlyCommentAndWritesHistoryAndAuditAt4000CharacterLimit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        var tag = new OrganizationClientTag
        {
            OrganizationId = organization.Id,
            Name = "Постоянный",
            NormalizedName = "ПОСТОЯННЫЙ"
        };
        client.Phone = "+7 900 123-45-67";
        client.PartyTypeCode = "company";
        client.Inn = "1234567890";
        client.Kpp = "123456789";
        client.LegalAddress = "Новосибирск";
        client.ResponsibleUserId = organization.OwnerUserId;
        client.StatusCode = "lead";
        client.Comment = "Старый комментарий";
        client.UpdatedAt = DateTime.UtcNow.AddDays(-1);
        client.TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = client,
            OrganizationClientTag = tag
        });
        var originalUpdatedAt = client.UpdatedAt;
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();

        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var comment = new string('я', 4000);
        await command.UpdateCommentAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientCommentDto
        {
            Comment = $" {comment} "
        });

        context.ChangeTracker.Clear();
        var savedClient = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(comment, savedClient.Comment);
        Assert.Equal("Клиент", savedClient.Name);
        Assert.Equal("+7 900 123-45-67", savedClient.Phone);
        Assert.Equal("company", savedClient.PartyTypeCode);
        Assert.Equal("1234567890", savedClient.Inn);
        Assert.Equal("123456789", savedClient.Kpp);
        Assert.Equal("Новосибирск", savedClient.LegalAddress);
        Assert.Equal(organization.OwnerUserId, savedClient.ResponsibleUserId);
        Assert.Equal("lead", savedClient.StatusCode);
        Assert.True(savedClient.UpdatedAt > originalUpdatedAt);
        Assert.Equal("Постоянный", await context.OrganizationClientTagAssignments.AsNoTracking()
            .Where(assignment => assignment.OrganizationClientId == client.Id)
            .Select(assignment => assignment.OrganizationClientTag.Name)
            .SingleAsync());
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == client.Id);
        Assert.Equal("updated", history.EventType);
        Assert.Equal("Комментарий клиента обновлен.", history.Description);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(item => item.TargetId == client.Id.ToString("D"));
        Assert.Equal("clients.updated", audit.EventType);
    }

    [Fact]
    public async Task RejectsCommentOver4000WithoutChangingClientOrWritingHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        client.Comment = "Сохранённый текст";
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();

        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateCommentAsync(
            organization.OwnerUserId,
            organization.Id,
            client.Id,
            new UpdateOrganizationClientCommentDto { Comment = new string('я', 4001) }));

        Assert.Equal("Комментарий не должен превышать 4000 символов.", exception.Message);
        Assert.Equal("Сохранённый текст", await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == client.Id)
            .Select(item => item.Comment)
            .SingleAsync());
        Assert.Empty(await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.OrganizationClientId == client.Id)
            .ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.TargetId == client.Id.ToString("D"))
            .ToListAsync());
    }

    [Fact]
    public async Task ClearsCommentAndDoesNotWriteDuplicateHistoryForUnchangedComment()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        client.Comment = "Старый комментарий";
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();

        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        await command.UpdateCommentAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientCommentDto { Comment = "  " });
        await command.UpdateCommentAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientCommentDto { Comment = null });

        Assert.Null(await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == client.Id)
            .Select(item => item.Comment)
            .SingleAsync());
        Assert.Single(await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.OrganizationClientId == client.Id)
            .ToListAsync());
    }
}
