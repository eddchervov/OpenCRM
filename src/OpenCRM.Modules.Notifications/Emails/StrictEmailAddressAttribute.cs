using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Атрибут строгой проверки адреса email. </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrictEmailAddressAttribute : ValidationAttribute
{
    /// <summary> Проверяет значение как строгий адрес email. </summary>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        return EmailAddressValidation.TryValidate(value.ToString(), out var errorMessage)
            ? ValidationResult.Success
            : new ValidationResult(errorMessage, [validationContext.MemberName!]);
    }
}
