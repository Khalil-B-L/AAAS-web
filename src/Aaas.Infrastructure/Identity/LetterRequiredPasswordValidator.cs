using Microsoft.AspNetCore.Identity;

namespace Aaas.Infrastructure.Identity;

/// <summary>
/// R21 : au moins une lettre, quelle que soit sa casse. Identity ne sait exiger qu'une minuscule
/// ou qu'une majuscule (RequireLowercase/RequireUppercase), d'où ce validateur.
/// </summary>
public sealed class LetterRequiredPasswordValidator : IPasswordValidator<AppUser>
{
    public const string ErrorCode = "PasswordRequiresLetter";

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        var result = password is not null && password.Any(char.IsLetter)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError
            {
                Code = ErrorCode,
                Description = "Le mot de passe doit contenir au moins une lettre.",
            });
        return Task.FromResult(result);
    }
}
