using Payments.Service.Configuration;

namespace Payments.Tests.Unit;

public sealed class ConfigurationTests
{
    [Fact]
    public void DisabledModeDoesNotRequireProviderCredentials()
    {
        Assert.True(ServiceRegistration.IsValid(ConfigurationTests.CreateOptions()));
    }

    [Fact]
    public void ApplicationIdDefaultsToOpenCrm()
    {
        Assert.Equal("opencrm", new PaymentOptions().ApplicationId);
    }

    [Fact]
    public void ApplicationIdCanBeConfiguredPerProduct()
    {
        var options = ConfigurationTests.CreateOptions();
        options.ApplicationId = "sample-crm";

        Assert.True(ServiceRegistration.IsValid(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("OpenCRM")]
    [InlineData("-sample-crm")]
    [InlineData("sample-crm.test")]
    [InlineData("sample-crm_test")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmn")]
    public void InvalidApplicationIdsAreRejected(string applicationId)
    {
        var options = ConfigurationTests.CreateOptions();
        options.ApplicationId = applicationId;

        Assert.False(ServiceRegistration.IsValid(options));
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("YooKassa")]
    [InlineData("")]
    public void UnsupportedModesAreRejected(string mode)
    {
        var options = ConfigurationTests.CreateOptions();
        options.Mode = mode;

        Assert.False(ServiceRegistration.IsValid(options));
    }

    [Fact]
    public void LiveCredentialsCannotEnableTestMode()
    {
        var options = ConfigurationTests.CreateOptions();
        options.Mode = "YooKassaTest";
        options.MerchantAccountId = "123456";
        options.SecretKey = "live_forbidden";

        Assert.False(ServiceRegistration.IsValid(options));
    }

    [Fact]
    public void TestModeRequiresExplicitTestCredentials()
    {
        var options = ConfigurationTests.CreateOptions();
        options.Mode = "YooKassaTest";
        options.IsTest = true;
        options.MerchantAccountId = "123456";
        options.SecretKey = "test_local_configuration_only";

        Assert.True(ServiceRegistration.IsValid(options));
    }

    [Fact]
    public void LiveModeRequiresLiveCredentialsAndLivePayments()
    {
        var options = ConfigurationTests.CreateOptions();
        options.Mode = "YooKassaLive";
        options.IsTest = false;
        options.MerchantAccountId = "123456";
        options.SecretKey = "live_local_configuration_only";

        Assert.True(ServiceRegistration.IsValid(options));
    }

    [Theory]
    [InlineData("YooKassaTest", true, "123456", "live_key")]
    [InlineData("YooKassaTest", false, "123456", "test_key")]
    [InlineData("YooKassaLive", false, "123456", "test_key")]
    [InlineData("YooKassaLive", true, "123456", "live_key")]
    [InlineData("YooKassaLive", false, "shop-1", "live_key")]
    public void ProviderModeRejectsWrongEnvironmentOrMerchant(
        string mode,
        bool isTest,
        string merchantAccountId,
        string secretKey)
    {
        var options = ConfigurationTests.CreateOptions();
        options.Mode = mode;
        options.IsTest = isTest;
        options.MerchantAccountId = merchantAccountId;
        options.SecretKey = secretKey;

        Assert.False(ServiceRegistration.IsValid(options));
    }

    [Theory]
    [InlineData("http://opencrm.example.invalid/payment/complete")]
    [InlineData("https://user:contact@example.invalid/payment/complete")]
    [InlineData("/payment/complete")]
    public void UnsafeReturnUrlsAreRejected(string returnUrl)
    {
        var options = ConfigurationTests.CreateOptions();
        options.AllowedReturnUrls = [returnUrl];

        Assert.False(ServiceRegistration.IsValid(options));
    }

    private static PaymentOptions CreateOptions() => new()
    {
        ServiceKey = new string('x', 40),
        MerchantAccountId = "disabled",
        AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"]
    };
}
