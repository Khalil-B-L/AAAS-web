using Microsoft.AspNetCore.Identity;

namespace Aaas.Infrastructure.Identity;

/// <summary>Messages d'Identity en français (R27). Seuls ceux qui peuvent atteindre l'utilisateur sont traduits.</summary>
public sealed class FrenchIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        Error(nameof(DefaultError), "Une erreur inattendue est survenue.");

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "Le compte a été modifié entre-temps. Veuillez réessayer.");

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), "L'e-mail n'est pas valide.");

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), "L'e-mail n'est pas valide.");

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), "Un compte existe déjà avec cet e-mail.");

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), "Un compte existe déjà avec cet e-mail.");

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), $"Le mot de passe doit contenir au moins {length} caractères.");

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), "Le mot de passe doit contenir au moins un chiffre.");

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), "Le mot de passe doit contenir au moins une lettre minuscule.");

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), "Le mot de passe doit contenir au moins une lettre majuscule.");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "Le mot de passe doit contenir au moins un caractère spécial.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents.");

    public override IdentityError UserAlreadyInRole(string role) =>
        Error(nameof(UserAlreadyInRole), $"L'utilisateur a déjà le rôle {role}.");

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), "Le blocage n'est pas activé pour ce compte.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
