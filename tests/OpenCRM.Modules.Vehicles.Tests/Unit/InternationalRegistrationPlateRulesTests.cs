using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class InternationalRegistrationPlateRulesTests
{
    [Theory]
    [InlineData("cn", "CN")]
    [InlineData(" GB ", "GB")]
    [InlineData("RU", "RU")]
    [InlineData(" zz ", "ZZ")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void CountryCodeIsValidatedWithoutGuessingFromLanguage(string? value, string? expected)
    {
        Assert.Equal(expected, InternationalRegistrationPlateRules.NormalizeCountryCode(value));
    }

    [Theory]
    [InlineData("Россия")]
    [InlineData("XX")]
    [InlineData("123")]
    public void UnsupportedCountryCodeIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => InternationalRegistrationPlateRules.NormalizeCountryCode(value));
    }

    [Theory]
    [InlineData(" 沪A·12345 ", "沪A·12345")]
    [InlineData("AB-123 CD", "AB-123 CD")]
    [InlineData("АВ123", "АВ123")]
    public void ForeignPlatePreservesLettersAndSeparators(string value, string expected)
    {
        Assert.Equal(expected, InternationalRegistrationPlateRules.NormalizePlate(value));
    }

    [Theory]
    [InlineData("A\n123")]
    [InlineData("A\u202e123")]
    [InlineData("<script>")]
    [InlineData(" - · ")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXY")]
    public void UnsafeOrUnsupportedPlateIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => InternationalRegistrationPlateRules.NormalizePlate(value));
    }
}
