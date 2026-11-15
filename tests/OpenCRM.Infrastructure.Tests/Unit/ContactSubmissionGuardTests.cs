using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Models.Home;

namespace OpenCRM.Infrastructure.Tests.Unit;

public class ContactSubmissionGuardTests
{
    private static readonly DateTime s_now = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AcceptsOrdinaryMessage()
    {
        var model = CreateModel();

        var errors = ContactSubmissionGuard.Validate(model, s_now);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidRenderTime(long ticks)
    {
        var model = CreateModel();
        model.FormRenderedAtTicks = ticks;

        var errors = ContactSubmissionGuard.Validate(model, s_now);

        Assert.Contains(errors, error => error.Code == "timing");
    }

    [Fact]
    public void RejectsAutomatedSignals()
    {
        var model = CreateModel();
        model.Website = "https://spam.example";
        model.FormRenderedAtTicks = s_now.AddSeconds(-1).Ticks;
        model.Text = "http://one.example http://two.example http://three.example http://four.example";

        var errors = ContactSubmissionGuard.Validate(model, s_now);

        Assert.Equal(["honeypot", "timing", "links"], errors.Select(error => error.Code));
    }

    private static ContactSupportViewModel CreateModel() => new()
    {
        FormRenderedAtTicks = s_now.AddSeconds(-5).Ticks,
        Text = "Хочу уточнить порядок работы с заказом."
    };
}
