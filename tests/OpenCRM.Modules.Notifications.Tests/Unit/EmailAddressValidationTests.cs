using System.ComponentModel.DataAnnotations;
using OpenCRM.Modules.Notifications.Emails;

namespace OpenCRM.Modules.Notifications.Tests.Unit;

public sealed class EmailAddressValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_EmptyAddress_ReturnsFalse(string? email)
    {
        var isValid = EmailAddressValidation.TryValidate(email, out var errorMessage);

        Assert.False(isValid);
        Assert.Equal(EmailAddressValidation.DefaultErrorMessage, errorMessage);
    }

    [Theory]
    [InlineData("first@@example.com")]
    [InlineData("first@second@example.com")]
    public void TryValidate_MultipleAtSigns_ReturnsSpecificError(string email)
    {
        var isValid = EmailAddressValidation.TryValidate(email, out var errorMessage);

        Assert.False(isValid);
        Assert.Equal("Email должен содержать только один символ @. Удалите лишний @.", errorMessage);
    }

    [Theory]
    [InlineData("@example.com")]
    [InlineData("person@")]
    [InlineData("person.example.com")]
    public void TryValidate_MissingLocalPartOrDomain_ReturnsDefaultError(string email)
    {
        var isValid = EmailAddressValidation.TryValidate(email, out var errorMessage);

        Assert.False(isValid);
        Assert.Equal(EmailAddressValidation.DefaultErrorMessage, errorMessage);
    }

    [Fact]
    public void TryValidate_LocalPartEndsWithDot_ReturnsSuggestedAddress()
    {
        var isValid = EmailAddressValidation.TryValidate("person..@example.com", out var errorMessage);

        Assert.False(isValid);
        Assert.Equal("Перед @ не должно быть точки. Возможно, вы имели в виду «person@example.com».", errorMessage);
    }

    [Fact]
    public void TryValidate_LocalPartStartsWithDot_ReturnsSpecificError()
    {
        var isValid = EmailAddressValidation.TryValidate(".person@example.com", out var errorMessage);

        Assert.False(isValid);
        Assert.Equal("Email не может начинаться с точки. Проверьте адрес и попробуйте ещё раз.", errorMessage);
    }

    [Fact]
    public void TryValidate_ConsecutiveLocalPartDots_ReturnsSpecificError()
    {
        var isValid = EmailAddressValidation.TryValidate("per..son@example.com", out var errorMessage);

        Assert.False(isValid);
        Assert.Equal("В email не должно быть двух точек подряд. Проверьте адрес и попробуйте ещё раз.", errorMessage);
    }

    [Theory]
    [InlineData("Person <person@example.com>")]
    [InlineData("person name@example.com")]
    [InlineData("person@invalid domain.com")]
    public void TryValidate_InvalidMailboxSyntax_ReturnsDefaultError(string email)
    {
        var isValid = EmailAddressValidation.TryValidate(email, out var errorMessage);

        Assert.False(isValid);
        Assert.Equal(EmailAddressValidation.DefaultErrorMessage, errorMessage);
    }

    [Fact]
    public void TryValidate_ValidAddressWithSurroundingWhitespace_ReturnsTrue()
    {
        var isValid = EmailAddressValidation.TryValidate("  Person.Name+tag@example.com  ", out _);

        Assert.True(isValid);
    }

    [Fact]
    public void StrictEmailAddressAttribute_NullValue_IsValid()
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateProperty(
            null,
            new ValidationContext(new EmailAddressModel()) { MemberName = nameof(EmailAddressModel.Email) },
            validationResults);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void StrictEmailAddressAttribute_ValidValue_IsValid()
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateProperty(
            "person@example.com",
            new ValidationContext(new EmailAddressModel()) { MemberName = nameof(EmailAddressModel.Email) },
            validationResults);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void StrictEmailAddressAttribute_InvalidValue_ReturnsMemberName()
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateProperty(
            ".person@example.com",
            new ValidationContext(new EmailAddressModel()) { MemberName = nameof(EmailAddressModel.Email) },
            validationResults);

        Assert.False(isValid);
        var validationResult = Assert.Single(validationResults);
        Assert.Equal([nameof(EmailAddressModel.Email)], validationResult.MemberNames);
        Assert.Equal(
            "Email не может начинаться с точки. Проверьте адрес и попробуйте ещё раз.",
            validationResult.ErrorMessage);
    }

    private sealed class EmailAddressModel
    {
        [StrictEmailAddress]
        public string? Email { get; set; }
    }
}
