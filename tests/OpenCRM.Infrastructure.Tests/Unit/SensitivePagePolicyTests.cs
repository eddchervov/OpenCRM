using Microsoft.AspNetCore.Http;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class SensitivePagePolicyTests
{
    [Theory]
    [InlineData("/auth/resetpassword", "", true)]
    [InlineData("/AUTH/CONFIRMEMAIL/", "", true)]
    [InlineData("/auth/confirmemailchange", "", true)]
    [InlineData("/email/unsubscribe", "", true)]
    [InlineData("/auth/register", "?Email=test%40example.test", true)]
    [InlineData("/organizations/join", "?token=synthetic-token", true)]
    [InlineData("/auth/login", "?CODE=synthetic-code", true)]
    [InlineData("/auth/login", "?userId=synthetic-user", true)]
    [InlineData("/auth/login", "?ReturnUrl=%2Forganizations%2Fjoin%3Ftoken%3Dsynthetic-token", true)]
    [InlineData("/auth/register", "?utm_source=site", false)]
    [InlineData("/articles", "?page=2", false)]
    public void SensitiveAddressesAreExcludedWithoutDisablingOrdinaryMarketing(string path, string query, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/opencrm";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        Assert.Equal(expected, SensitivePagePolicy.IsSensitive(context.Request));
    }
}
