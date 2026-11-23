using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Common.Validation;

/// <summary>Champ « version » : Base64 d'un rowversion (8 octets), tel que renvoyé par l'API (ADR 0006).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RowVersionAttribute : ValidationAttribute
{
    public const string InvalidMessage = "La version de la fiche est invalide. Rechargez-la.";

    public RowVersionAttribute()
        : base(InvalidMessage)
    {
    }

    // Vide : laissé à [Required].
    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || RowVersions.TryDecode(text, out _);
}
