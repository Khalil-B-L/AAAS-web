using System.ComponentModel.DataAnnotations;
using Aaas.Domain.Common;

namespace Aaas.Application.Common.Validation;

/// <summary>
/// Vérifie le format RG-11 (« +216 » + 8 chiffres, mobile ou fixe) en déléguant au Domain : une seule source de vérité.
/// Sert au téléphone des membres et au numéro de contact de l'association.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class TunisianPhoneNumberAttribute : ValidationAttribute
{
    public TunisianPhoneNumberAttribute()
        : base(PhoneNumber.ExpectedFormatMessage)
    {
    }

    // Une valeur vide est laissée à [Required], pour n'afficher qu'un message.
    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || PhoneNumber.TryParse(text, out _);
}
