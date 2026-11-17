using System.ComponentModel.DataAnnotations;
using Aaas.Domain.Common;

namespace Aaas.Application.Accounts.Validation;

/// <summary>Vérifie le format RG-11 en déléguant au Domain : une seule source de vérité pour la règle.</summary>
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

/// <summary>Case à cocher obligatoire (consentement).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}
