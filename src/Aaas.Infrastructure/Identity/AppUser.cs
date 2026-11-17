using Microsoft.AspNetCore.Identity;

namespace Aaas.Infrastructure.Identity;

/// <summary>
/// Compte Identity (table AspNetUsers). Données limitées à R20 et au § 6.4 du cahier :
/// prénom, nom, e-mail, téléphone, mot de passe haché, création, dernière connexion, consentement (date + version).
/// UserName = Email ; PhoneNumber (colonne Identity) stocké normalisé : +21620123456.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset PrivacyConsentAt { get; set; }
    public string PrivacyPolicyVersion { get; set; } = string.Empty;

    /// <summary>Mise à jour à chaque connexion réussie ; sert à repérer les comptes inactifs depuis 3 ans (R25).</summary>
    public DateTimeOffset? LastLoginAt { get; set; }
}
