using OpenCRM.Infrastructure.Payments.ServiceIntegration;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class PaymentsServiceOptionsTests
{
    [Fact]
    public void ApplicationIdDefaultsToOpenCrmAndDefinesQueueNames()
    {
        var options = new PaymentsServiceOptions();

        Assert.Equal("opencrm", options.ApplicationId);
        Assert.Equal("payments-opencrm-commands", options.CommandsQueueName);
        Assert.Equal("payments-opencrm-events", options.EventsQueueName);
    }

    [Fact]
    public void ApplicationIdCanSelectProductQueues()
    {
        var options = new PaymentsServiceOptions { ApplicationId = "sample-crm" };

        Assert.True(PaymentsServiceOptions.IsValid(options));
        var enabledOptions = PaymentsServiceOptionsTests.CreateEnabledOptions("https://payments-service.internal");
        enabledOptions.ApplicationId = "sample-crm";
        Assert.True(PaymentsServiceOptions.IsValid(enabledOptions));
        Assert.Equal("payments-sample-crm-commands", options.CommandsQueueName);
        Assert.Equal("payments-sample-crm-events", options.EventsQueueName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OpenCRM")]
    [InlineData("-sample-crm")]
    [InlineData("sample-crm.test")]
    [InlineData("sample-crm_test")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmn")]
    public void InvalidApplicationIdsAreRejectedEvenWhenPaymentsAreDisabled(string applicationId)
    {
        var options = new PaymentsServiceOptions { ApplicationId = applicationId };

        Assert.False(PaymentsServiceOptions.IsValid(options));
    }

    [Fact]
    public void PaymentsServiceAllowsPlainHttpOnlyForTheExplicitInternalHost()
    {
        var options = CreateEnabledOptions("http://payments-service:8080");
        options.InternalHttpHost = "payments-service";

        Assert.True(PaymentsServiceOptions.IsValid(options));
    }

    [Theory]
    [InlineData("http://payments-service:8080", "")]
    [InlineData("http://other-service:8080", "payments-service")]
    public void PaymentsServiceRejectsUnapprovedHttpHosts(string baseUrl, string internalHttpHost)
    {
        var options = CreateEnabledOptions(baseUrl);
        options.InternalHttpHost = internalHttpHost;

        Assert.False(PaymentsServiceOptions.IsValid(options));
    }

    [Fact]
    public void PaymentsServiceAllowsHttpsWithoutAnInternalHostOptIn()
    {
        var options = CreateEnabledOptions("https://payments-service.internal");

        Assert.True(PaymentsServiceOptions.IsValid(options));
    }

    [Fact]
    public void PaymentsServiceAllowsLiveStoreExpectation()
    {
        var options = CreateEnabledOptions("https://payments-service.internal");
        options.ExpectedIsTest = false;

        Assert.True(PaymentsServiceOptions.IsValid(options));
    }

    private static PaymentsServiceOptions CreateEnabledOptions(string baseUrl) => new()
    {
        Enabled = true,
        ExpectedIsTest = true,
        MerchantAccountId = "123456",
        BaseUrl = baseUrl,
        ServiceKey = new string('x', 40),
        ReturnUrl = "https://opencrm.example.invalid/payment/complete",
        AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"],
        Host = "payments-rabbitmq",
        VirtualHost = "opencrm",
        Username ="opencrm",
        Password = "password"
    };
}