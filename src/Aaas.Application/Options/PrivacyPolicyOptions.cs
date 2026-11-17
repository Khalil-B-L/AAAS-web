using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Options;

/// <summary>
/// Version en vigueur de la politique de confidentialité, enregistrée avec la date du consentement (§ 6.4 du cahier).
/// À incrémenter à chaque modification du texte de la politique.
/// </summary>
public sealed class PrivacyPolicyOptions
{
    public const string SectionName = "PrivacyPolicy";

    [Required(ErrorMessage = "PrivacyPolicy:CurrentVersion est obligatoire.")]
    [MaxLength(20)]
    public string CurrentVersion { get; set; } = string.Empty;
}
