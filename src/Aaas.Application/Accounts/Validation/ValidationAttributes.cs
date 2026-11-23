using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Accounts.Validation;

// [TunisianPhoneNumber] a été déplacé dans Aaas.Application.Common.Validation (sert aussi au numéro de contact).

/// <summary>Case à cocher obligatoire (consentement).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}
