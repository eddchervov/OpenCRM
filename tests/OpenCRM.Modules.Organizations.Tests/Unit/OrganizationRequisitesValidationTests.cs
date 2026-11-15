using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class OrganizationRequisitesValidationTests
{
    [Fact]
    public void FilledRussianRequisitesPassValidation()
    {
        var request = JsonSerializer.Deserialize<UpdateOrganizationRequisitesDto>(
            """
            {
              "legalName": "Общество с ограниченной ответственностью «ТЕСТ-МЕДИА» (ООО «ТЕСТ-МЕДИА»)",
              "inn": "9901000003",
              "kpp": "990101001",
              "ogrn": "1999770000018",
              "phone": "+74950000000",
              "email": "test-company@example.com",
              "legalAddress": "101000, г. Москва, ул. Тестовая, д. 99, стр. 1, офис 999",
              "actualAddress": "101000, г. Москва, ул. Тестовая, д. 99, стр. 1, офис 999",
              "signatoryPosition": "Генеральный директор",
              "signatoryName": "Иванов Иван Иванович",
              "signatoryBasis": "На основании Устава"
            }
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("123", "123", "123", false)]
    [InlineData("1234567890", "1234AB123", "1234567890123", true)]
    [InlineData("123456789012", null, "123456789012345", true)]
    [InlineData(" 123456789012 ", " 1234ab123 ", " 123456789012345 ", true)]
    [InlineData("   ", " ", "", true)]
    [InlineData("１２３４５６７８９０", "1234АБ123", "123456789012A", false)]
    public void RussianFormatsApplyToBothUpdateContracts(string? inn, string? kpp, string? ogrn, bool expected)
    {
        object[] requests =
        [
            new UpdateOrganizationRequisitesDto { Inn = inn, Kpp = kpp, Ogrn = ogrn },
            new UpdateOrganizationDto { Name = "Тест", PrimaryScenarioCode = "detailing", Inn = inn, Kpp = kpp, Ogrn = ogrn }
        ];

        foreach (var request in requests)
        {
            var errors = new List<ValidationResult>();
            Assert.Equal(expected, Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        }
    }

    [Fact]
    public void OptionalEmptyRequisitesAreAcceptedAndInvalidFieldsHaveRussianMessages()
    {
        var request = new UpdateOrganizationRequisitesDto();
        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        request.Email = "invalid-email";
        request.Inn = new string('1', 13);

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Email))
            && error.ErrorMessage == "Укажите корректный адрес электронной почты.");
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Inn))
            && error.ErrorMessage == "ИНН должен содержать 10 или 12 цифр.");
    }
}
