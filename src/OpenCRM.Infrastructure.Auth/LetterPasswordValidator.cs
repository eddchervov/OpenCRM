using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Auth;

/// <summary> Требует хотя бы одну букву в новом пароле независимо от регистра. </summary>
public sealed class LetterPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        if (password?.Any(char.IsLetter) == true)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "PasswordRequiresLetter",
            Description = "Пароль должен содержать хотя бы одну букву."
        }));
    }
}
