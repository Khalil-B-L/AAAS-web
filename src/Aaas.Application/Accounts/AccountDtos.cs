using System.ComponentModel.DataAnnotations;
using Aaas.Application.Accounts.Validation;

namespace Aaas.Application.Accounts;

// Propriétés initialisées à "" plutôt que « required » : un champ absent du JSON doit produire
// une erreur de validation en français sur ce champ, pas une erreur de désérialisation globale.

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Le prénom est obligatoire.")]
    [MaxLength(100, ErrorMessage = "Le prénom ne doit pas dépasser 100 caractères.")]
    public string FirstName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [MaxLength(100, ErrorMessage = "Le nom ne doit pas dépasser 100 caractères.")]
    public string LastName { get; init; } = string.Empty;

    [Required(ErrorMessage = "L'e-mail est obligatoire.")]
    [EmailAddress(ErrorMessage = "L'e-mail n'est pas valide.")]
    [MaxLength(256, ErrorMessage = "L'e-mail ne doit pas dépasser 256 caractères.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Le numéro de téléphone est obligatoire.")]
    [TunisianPhoneNumber]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    [MaxLength(128, ErrorMessage = "Le mot de passe ne doit pas dépasser 128 caractères.")] // borne le coût du hachage
    public string Password { get; init; } = string.Empty;

    [MustBeTrue(ErrorMessage = "Vous devez accepter la politique de confidentialité pour créer un compte.")]
    public bool AcceptPrivacyPolicy { get; init; }
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "L'e-mail est obligatoire.")]
    [MaxLength(256, ErrorMessage = "L'e-mail ne doit pas dépasser 256 caractères.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    [MaxLength(128, ErrorMessage = "Le mot de passe ne doit pas dépasser 128 caractères.")]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Réponse de l'inscription et de la connexion : le jeton et le compte, pour éviter un second appel au front.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountDto User);

/// <summary>Données du compte visibles par son titulaire (US-F3). Jamais de hachage ni de security stamp.</summary>
public sealed record AccountDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset PrivacyConsentAt,
    string PrivacyPolicyVersion,
    DateTimeOffset? LastLoginAt)
{
    public static AccountDto From(UserAccount user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email,
        user.PhoneNumber,
        user.Roles,
        user.CreatedAt,
        user.PrivacyConsentAt,
        user.PrivacyPolicyVersion,
        user.LastLoginAt);
}
