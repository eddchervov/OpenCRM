using OpenCRM.Modules.Notifications.Emails;

namespace OpenCRM.Modules.Notifications.Tests.Unit;

public sealed class EmailUnsubscribeTokenTests
{
    private const string UserId = " user-123 ";
    private const string Email = " Person@example.com ";
    private const string Secret = "test-secret-that-is-long-enough-for-hmac";

    [Fact]
    public void Create_AndValidate_SameIdentity_ReturnsTrue()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, token, Secret);

        Assert.True(isValid);
    }

    [Fact]
    public void Create_TrimsIdentityAndIgnoresEmailCase()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        var isValid = EmailUnsubscribeToken.Validate("user-123", "person@EXAMPLE.COM", token, Secret);

        Assert.True(isValid);
    }

    [Fact]
    public void Create_ReturnsBase64UrlTokenWithoutPadding()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyToken_ReturnsFalse(string? token)
    {
        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, token!, Secret);

        Assert.False(isValid);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("not-a-token")]
    public void Validate_MalformedBase64UrlToken_ReturnsFalse(string token)
    {
        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, token, Secret);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ValidBase64UrlWithWrongSignatureLength_ReturnsFalse()
    {
        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, "AQ", Secret);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_DifferentUserId_ReturnsFalse()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        var isValid = EmailUnsubscribeToken.Validate("user-456", Email, token, Secret);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_DifferentEmail_ReturnsFalse()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        var isValid = EmailUnsubscribeToken.Validate(UserId, "other@example.com", token, Secret);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_DifferentSecret_ReturnsFalse()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);

        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, token, "another-secret");

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ChangedToken_ReturnsFalse()
    {
        var token = EmailUnsubscribeToken.Create(UserId, Email, Secret);
        var changedToken = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        var isValid = EmailUnsubscribeToken.Validate(UserId, Email, changedToken, Secret);

        Assert.False(isValid);
    }
}
