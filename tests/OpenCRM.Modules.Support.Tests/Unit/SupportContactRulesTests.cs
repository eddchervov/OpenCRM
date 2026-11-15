using OpenCRM.Modules.Support.Personal;

namespace OpenCRM.Modules.Support.Tests.Unit;

public sealed class SupportContactRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  +7 000 000-00-00  ", "+7 000 000-00-00")]
    [InlineData("1234567", "1234567")]
    [InlineData("123456789012345", "123456789012345")]
    public void OptionalPhonePreservesValidFormattingAndAcceptsLengthBoundaries(string? input, string? expected)
    {
        Assert.Equal(expected, SupportContactRules.NormalizePhone(input));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("1234567890123456")]
    [InlineData("+7 000 000-00-00 доб. 1")]
    [InlineData("123\t4567")]
    public void InvalidPhoneLengthOrCharactersAreRejected(string input)
    {
        Assert.Throws<InvalidOperationException>(() => SupportContactRules.NormalizePhone(input));
    }
}
