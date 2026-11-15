using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Vehicles;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class VehicleRegistrationPlatePolicyTests
{
    [Fact]
    public void NormalizeMapsLookalikeLettersWithoutChangingDigits()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        var normalized = policy.Normalize("a123bc000");

        Assert.Equal("А123ВС000", normalized);
        Assert.True(policy.MatchesFormat(normalized, VehicleKind.PassengerCar));
        policy.Validate(normalized, VehicleKind.PassengerCar);
    }

    [Theory]
    [InlineData("Б")]
    [InlineData("Г")]
    [InlineData("Д")]
    [InlineData("Ё")]
    [InlineData("Ж")]
    [InlineData("З")]
    [InlineData("И")]
    [InlineData("Й")]
    [InlineData("Л")]
    [InlineData("П")]
    [InlineData("Ф")]
    [InlineData("Ц")]
    [InlineData("Ч")]
    [InlineData("Ш")]
    [InlineData("Щ")]
    [InlineData("Ъ")]
    [InlineData("Ы")]
    [InlineData("Ь")]
    [InlineData("Э")]
    [InlineData("Ю")]
    [InlineData("Я")]
    [InlineData("D")]
    [InlineData("G")]
    public void StandardFormatsRejectUnissuedLettersWithoutReplacingThem(string letter)
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();
        var plates = new[]
        {
            (VehicleKind.PassengerCar, $"А123{letter}В000"),
            (VehicleKind.Trailer, $"{letter}В1234000"),
            (VehicleKind.Motorcycle, $"1234{letter}В000")
        };

        foreach (var (kind, plate) in plates)
        {
            Assert.False(policy.MatchesFormat(plate, kind));
            Assert.Throws<InvalidOperationException>(() => policy.Validate(plate, kind));
            Assert.Contains(letter, policy.Normalize(plate));
            policy.Validate(plate, kind, allowOtherFormat: true);
        }
    }

    [Fact]
    public void StandardFormatsAcceptEveryAllowedRussianLetter()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();
        foreach (var letter in "АВЕКМНОРСТУХ")
        {
            policy.Validate($"{letter}123{letter}{letter}000", VehicleKind.PassengerCar);
            policy.Validate($"{letter}{letter}1234000", VehicleKind.Trailer);
            policy.Validate($"1234{letter}{letter}000", VehicleKind.Motorcycle);
        }
    }

    [Fact]
    public void NormalizeDoesNotApplyVehicleFormatValidation()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        var normalized = policy.Normalize("А123ВС1");

        Assert.Equal("А123ВС1", normalized);
        Assert.False(policy.MatchesFormat(normalized, VehicleKind.PassengerCar));
    }

    [Fact]
    public void NormalizePreservesSafeAlternatePlateTextWhenExplicitlyAllowed()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        var normalized = policy.Normalize(" a-123вс 1 ", allowOtherFormat: true);

        Assert.Equal("a-123вс 1", normalized);
        policy.Validate(normalized, VehicleKind.PassengerCar, allowOtherFormat: true);
    }

    [Theory]
    [InlineData(VehicleKind.PassengerCar, "1234АА154")]
    [InlineData(VehicleKind.Motorcycle, "А123ВС154")]
    [InlineData(VehicleKind.Trailer, "А123ВС154")]
    public void ValidateRejectsNumbersThatDoNotMatchTheSelectedVehicleKind(VehicleKind kind, string registrationPlate)
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        Assert.Throws<InvalidOperationException>(() => policy.Validate(registrationPlate, kind));
    }

    [Fact]
    public void ValidateAllowsSafeAlternateFormatsOnlyWhenRequestedOrForUnrestrictedKinds()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        Assert.Throws<InvalidOperationException>(() => policy.Validate("РАМА-7", VehicleKind.PassengerCar));
        policy.Validate("РАМА-7", VehicleKind.PassengerCar, allowOtherFormat: true);
        policy.Validate("РАМА-7", VehicleKind.Boat);
        Assert.Throws<InvalidOperationException>(() => policy.Validate("РАМА/7", VehicleKind.Boat));
        Assert.Throws<InvalidOperationException>(() => policy.Validate("РАМА\t7", VehicleKind.Boat));
        Assert.Throws<InvalidOperationException>(() => policy.Validate("А", VehicleKind.Boat));
        Assert.Throws<InvalidOperationException>(() => policy.Validate("AB-123", (VehicleKind)999, allowOtherFormat: true));
    }

    [Fact]
    public void FormatsExposeNumericVehicleKindsAndConfiguredMasks()
    {
        var formats = VehicleRegistrationPlatePolicyTests.CreatePolicy().GetFormats();

        Assert.Equal(16, formats.MaxLength);
        Assert.Equal("[АВЕКМНОРСТУХ]", formats.LetterPattern);
        Assert.Equal(new[] { 1, 2 }, formats.Formats.Single(format => format.Code == "car").Kinds);
        Assert.Equal(new[] { "L###LL##", "L###LL###" }, formats.Formats.Single(format => format.Code == "car").Masks);
    }

    [Fact]
    public void GetSearchVariantsIncludesCanonicalLatinAndMixedPartialFormsWithinConfiguredLimit()
    {
        var policy = VehicleRegistrationPlatePolicyTests.CreatePolicy();

        var variants = policy.GetSearchVariants("А123AA");

        Assert.Contains("А123AA", variants);
        Assert.Contains("А123АА", variants);
        Assert.Contains("A123AA", variants);
        Assert.InRange(variants.Count, 1, 64);
        Assert.Contains("А", policy.GetSearchVariants("A"));
    }

    private static VehicleRegistrationPlatePolicy CreatePolicy()
    {
        var settings = new Dictionary<string, string?>
        {
            ["VehicleRegistrationPlates:MaxLength"] = "16",
            ["VehicleRegistrationPlates:RegexTimeoutMilliseconds"] = "100",
            ["VehicleRegistrationPlates:MaxSearchVariants"] = "64",
            ["VehicleRegistrationPlates:LetterPattern"] = "[АВЕКМНОРСТУХ]",
            ["VehicleRegistrationPlates:CharacterMappings:A"] = "А",
            ["VehicleRegistrationPlates:CharacterMappings:B"] = "В",
            ["VehicleRegistrationPlates:CharacterMappings:C"] = "С",
            ["VehicleRegistrationPlates:CharacterMappings:E"] = "Е",
            ["VehicleRegistrationPlates:CharacterMappings:H"] = "Н",
            ["VehicleRegistrationPlates:CharacterMappings:K"] = "К",
            ["VehicleRegistrationPlates:CharacterMappings:M"] = "М",
            ["VehicleRegistrationPlates:CharacterMappings:O"] = "О",
            ["VehicleRegistrationPlates:CharacterMappings:P"] = "Р",
            ["VehicleRegistrationPlates:CharacterMappings:T"] = "Т",
            ["VehicleRegistrationPlates:CharacterMappings:X"] = "Х",
            ["VehicleRegistrationPlates:CharacterMappings:Y"] = "У",
            ["VehicleRegistrationPlates:Formats:0:Code"] = "car",
            ["VehicleRegistrationPlates:Formats:0:Kinds:0"] = "1",
            ["VehicleRegistrationPlates:Formats:0:Kinds:1"] = "2",
            ["VehicleRegistrationPlates:Formats:0:Masks:0"] = "L###LL##",
            ["VehicleRegistrationPlates:Formats:0:Masks:1"] = "L###LL###",
            ["VehicleRegistrationPlates:Formats:0:Pattern"] = "^[АВЕКМНОРСТУХ][0-9]{3}[АВЕКМНОРСТУХ]{2}[0-9]{2,3}$",
            ["VehicleRegistrationPlates:Formats:0:Example"] = "А123ВС154",
            ["VehicleRegistrationPlates:Formats:1:Code"] = "trailer",
            ["VehicleRegistrationPlates:Formats:1:Kinds:0"] = "10",
            ["VehicleRegistrationPlates:Formats:1:Masks:0"] = "LL######",
            ["VehicleRegistrationPlates:Formats:1:Masks:1"] = "LL#######",
            ["VehicleRegistrationPlates:Formats:1:Pattern"] = "^[АВЕКМНОРСТУХ]{2}[0-9]{6,7}$",
            ["VehicleRegistrationPlates:Formats:1:Example"] = "АА1234154",
            ["VehicleRegistrationPlates:Formats:2:Code"] = "motorcycle-tractor",
            ["VehicleRegistrationPlates:Formats:2:Kinds:0"] = "3",
            ["VehicleRegistrationPlates:Formats:2:Kinds:1"] = "4",
            ["VehicleRegistrationPlates:Formats:2:Kinds:2"] = "5",
            ["VehicleRegistrationPlates:Formats:2:Kinds:3"] = "6",
            ["VehicleRegistrationPlates:Formats:2:Kinds:4"] = "9",
            ["VehicleRegistrationPlates:Formats:2:Masks:0"] = "####LL##",
            ["VehicleRegistrationPlates:Formats:2:Masks:1"] = "####LL###",
            ["VehicleRegistrationPlates:Formats:2:Pattern"] = "^[0-9]{4}[АВЕКМНОРСТУХ]{2}[0-9]{2,3}$",
            ["VehicleRegistrationPlates:Formats:2:Example"] = "1234АА154"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new VehicleRegistrationPlatePolicy(configuration);
    }
}
