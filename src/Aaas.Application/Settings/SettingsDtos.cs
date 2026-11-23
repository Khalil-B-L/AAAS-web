using System.ComponentModel.DataAnnotations;
using Aaas.Application.Common;
using Aaas.Application.Common.Validation;
using Aaas.Domain.Settings;

namespace Aaas.Application.Settings;

/// <summary>Numéro de contact (US-E8, US-D4). Lisible par tout membre connecté, jamais par un visiteur (RG-10).</summary>
public sealed record ContactSettingsDto(
    string? PhoneNumber,
    bool WhatsApp,
    bool IsConfigured,
    string Version,
    DateTimeOffset? UpdatedAt)
{
    public static ContactSettingsDto From(AssociationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(
            settings.ContactPhone,
            settings.ContactWhatsApp,
            settings.IsContactConfigured,
            RowVersions.Encode(settings.RowVersion),
            settings.UpdatedAt);
    }
}

public sealed class UpdateContactSettingsRequest
{
    [Required(ErrorMessage = "Le numéro de téléphone est obligatoire.")]
    [TunisianPhoneNumber]
    public string PhoneNumber { get; init; } = string.Empty;

    public bool WhatsApp { get; init; }

    [Required(ErrorMessage = "La version de la fiche est obligatoire. Rechargez-la.")]
    [RowVersion]
    public string Version { get; init; } = string.Empty;
}

/// <summary>Retrait du numéro : seule la version est envoyée (contrôle de concurrence, comme pour la modification).</summary>
public sealed class RemoveContactSettingsRequest
{
    [Required(ErrorMessage = "La version de la fiche est obligatoire. Rechargez-la.")]
    [RowVersion]
    public string Version { get; init; } = string.Empty;
}
